using System.Net;
using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OAuth.Discord;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pizon.Web.Auth;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Services;

namespace Pizon.Web.Tests;

/// <summary>
/// Tests for the presence hub over a real SignalR connection.
/// </summary>
public sealed class NodeHubTests : IAsyncDisposable
{
    private readonly BbsFactory factory = new();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    /// <summary>Each caller gets a node; everyone hears the busy list change as callers come and go.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task TracksNodes()
    {
        var first = await ConnectAsync();
        var second = await ConnectAsync();

        Assert.Equal(1, await first.Assigned.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, await second.Assigned.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await WaitForAsync(first, [1, 2]);

        await second.Connection.DisposeAsync();
        await WaitForAsync(first, [1]);
        await first.Connection.DisposeAsync();
    }

    private static async Task WaitForAsync(Probe probe, int[] expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!probe.Latest.SequenceEqual(expected))
        {
            Assert.True(DateTime.UtcNow < deadline, $"nodes were [{string.Join(',', probe.Latest)}]");
            await Task.Delay(20);
        }
    }

    private async Task<Probe> ConnectAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, PresenceProtocol.HubPath), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var probe = new Probe(connection);
        connection.On<int>(PresenceProtocol.AssignedMessage, n => probe.Assigned.TrySetResult(n));
        connection.On<int[]>(PresenceProtocol.NodesMessage, nodes => probe.Latest = nodes);
        await connection.StartAsync();
        return probe;
    }

    /// <summary>
    /// One test connection and what it heard.
    /// </summary>
    /// <param name="connection">The connection.</param>
    private sealed class Probe(HubConnection connection)
    {
        /// <summary>Gets the connection.</summary>
        public HubConnection Connection { get; } = connection;

        /// <summary>Gets the node assignment.</summary>
        public TaskCompletionSource<int> Assigned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets or sets the latest busy list.</summary>
        public int[] Latest { get; set; } = [];
    }
}

/// <summary>
/// Tests for Discord sign-in wiring, with placeholder (non-working) app credentials.
/// </summary>
public sealed class DiscordTests : IAsyncDisposable
{
    private readonly BbsFactory factory = new(new Dictionary<string, string?>
    {
        ["Discord:ClientId"] = "test-client-id",
        ["Discord:ClientSecret"] = "test-client-secret",
        ["SysOp:DiscordUserId"] = "4242",
    });

    /// <inheritdoc />
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    /// <summary>Sign-in challenges to Discord with only the identify scope; /api/me advertises it.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ChallengesToDiscord()
    {
        using var client = factory.Caller();
        Assert.True((await client.Get<MeResponse>("/api/me")).DiscordEnabled);
        using var response = await client.GetAsync(new Uri("/auth/discord?returnUrl=/?resume=login", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.AbsoluteUri;
        Assert.StartsWith("https://discord.com/", location, StringComparison.Ordinal);
        Assert.Contains("scope=identify&", location + "&", StringComparison.Ordinal);
    }

    /// <summary>A failed or forged callback lands on the login-failed screen.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task FailedCallbackSaysLoginFailed()
    {
        using var client = factory.Caller();
        using var response = await client.GetAsync(new Uri("/signin-discord?code=x&state=forged", UriKind.Relative));
        Assert.Equal(AuthSetup.LoginFailedUrl, response.Headers.Location!.OriginalString);
    }

    /// <summary>
    /// When Discord answers, the caller is recorded and gets a BBS principal; the configured id is the SysOp.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task TicketCreatesTheCaller()
    {
        using var client = factory.Caller();
        await using var scope = factory.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<DiscordAuthenticationOptions>>().Get(DiscordAuthenticationDefaults.AuthenticationScheme);
        var schemes = scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = (await schemes.GetSchemeAsync(DiscordAuthenticationDefaults.AuthenticationScheme))!;
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        using var user = JsonDocument.Parse("""{ "id": "4242", "global_name": "David", "username": "dp" }""");
        using var tokens = OAuthTokenResponse.Success(JsonDocument.Parse("""{ "access_token": "t" }"""));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "4242")], "Discord");
        var ok = new OAuthCreatingTicketContext(new ClaimsPrincipal(identity), new AuthenticationProperties(), http, scheme, options, new HttpClient(), tokens, user.RootElement);
        await options.Events.CreatingTicket(ok);
        var uid = ok.Principal!.FindFirst(BbsClaims.UserId)!.Value;
        var stored = await factory.WithDbAsync(db => db.Users.SingleAsync());
        Assert.Equal(stored.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), uid);
        Assert.Equal("David", stored.DiscordName);
        var callers = scope.ServiceProvider.GetRequiredService<CallerDirectory>();
        Assert.True(callers.IsSysop(stored));

        var missing = new OAuthCreatingTicketContext(new ClaimsPrincipal(new ClaimsIdentity("Discord")), new AuthenticationProperties(), http, scheme, options, new HttpClient(), tokens, user.RootElement);
        await options.Events.CreatingTicket(missing);
        Assert.NotNull(missing.Result?.Failure);
    }
}
