using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Pizon.Doors.Sdk;
using Pizon.Web.Data;
using Pizon.Web.Services;

namespace Pizon.Web.Tests;

/// <summary>
/// Records notifications instead of sending them.
/// </summary>
internal sealed class RecordingNotifier : INotifier
{
    /// <summary>Gets the notifications sent, as "title|message".</summary>
    public List<string> Sent { get; } = [];

    /// <inheritdoc />
    public Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        lock (Sent)
        {
            Sent.Add(title + "|" + message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// A door catalog with one daily-count door (<c>oracle</c>, max 3) and one max-score door (<c>maze</c>, max 100).
/// </summary>
internal sealed class TestCatalog : IDoorCatalog
{
    private readonly DoorManifest manifest = DoorManifest.Parse("""
        { "doors": [
          { "id": "oracle", "title": "Oracle", "assemblies": ["a.wasm"], "entryType": "A", "scored": true, "scoreMode": "dailyCount", "maxScore": 3 },
          { "id": "maze", "title": "Maze", "assemblies": ["b.wasm"], "entryType": "B", "scored": true, "maxScore": 100 },
          { "id": "plain", "title": "Plain", "assemblies": ["c.wasm"], "entryType": "C" }
        ] }
        """);

    /// <inheritdoc />
    public DoorManifestEntry? FindScored(string id) => manifest.Find(id) is { Scored: true } e ? e : null;
}

/// <summary>
/// A clock fixed at a settable time.
/// </summary>
internal sealed class FixedClock : TimeProvider
{
    /// <summary>Gets or sets the current time.</summary>
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Gets today's date (UTC) as the API expects it.</summary>
    public string Today => Now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// A minimal web host environment over a folder.
/// </summary>
/// <param name="webRoot">The web root folder.</param>
internal sealed class FolderEnvironment(string webRoot) : IWebHostEnvironment
{
    /// <inheritdoc />
    public string WebRootPath { get; set; } = webRoot;

    /// <inheritdoc />
    public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(webRoot);

    /// <inheritdoc />
    public string ApplicationName { get; set; } = "Pizon.Web";

    /// <inheritdoc />
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

    /// <inheritdoc />
    public string ContentRootPath { get; set; } = webRoot;

    /// <inheritdoc />
    public string EnvironmentName { get; set; } = Environments.Production;
}

/// <summary>
/// Paths to the repository's real content, for tests that check the shipped files.
/// </summary>
internal static class RepoPaths
{
    /// <summary>Gets the <c>src</c> folder.</summary>
    public static string Src { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>Gets the client's <c>wwwroot</c>.</summary>
    public static string ClientWwwroot { get; } = Path.Combine(Src, "Pizon.Web.Client", "wwwroot");
}

/// <summary>
/// Hosts the real server in Development with a throwaway SQLite database and test fakes.
/// </summary>
internal sealed class BbsFactory : WebApplicationFactory<Program>
{
    private static int ip;
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pizon-bbs-tests", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> settings;
    private readonly Action<IServiceCollection>? services;

    /// <summary>
    /// Initializes a new instance of the <see cref="BbsFactory"/> class.
    /// </summary>
    /// <param name="settings">Extra configuration (for example Discord settings).</param>
    /// <param name="environment">The environment name.</param>
    /// <param name="services">Extra service registrations, applied last.</param>
    public BbsFactory(Dictionary<string, string?>? settings = null, string environment = "Development", Action<IServiceCollection>? services = null)
    {
        this.settings = settings ?? [];
        this.services = services;
        EnvironmentName = environment;
    }

    /// <summary>Gets the environment name.</summary>
    public string EnvironmentName { get; }

    /// <summary>Gets the notifier.</summary>
    public RecordingNotifier Notifier { get; } = new();

    /// <summary>Gets the clock.</summary>
    public FixedClock Clock { get; } = new();

    /// <summary>Gets the database file.</summary>
    public string DatabasePath => Path.Combine(directory, "bbs.db");

    /// <summary>
    /// Creates a client that keeps cookies, doesn't follow redirects and calls from its own IP address.
    /// </summary>
    /// <returns>The client.</returns>
    public HttpClient Caller()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.0.{Interlocked.Increment(ref ip) / 250}.{(ip % 250) + 1}");
        return client;
    }

    /// <summary>
    /// Signs in through the Development sign-in, optionally choosing a handle.
    /// </summary>
    /// <param name="user"><c>tester</c> or <c>sysop</c>.</param>
    /// <param name="handle">The handle to choose, or <see langword="null"/> to stay a new user.</param>
    /// <returns>The signed-in client.</returns>
    public async Task<HttpClient> SignInAsync(string user, string? handle)
    {
        var client = Caller();
        using var response = await client.GetAsync(new Uri($"/auth/dev?user={user}&returnUrl=/next", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/next", response.Headers.Location!.OriginalString);
        if (handle is not null)
        {
            using var set = await client.PostJson("/api/me/handle", new { handle });
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        }

        return client;
    }

    /// <summary>
    /// Runs code against the database.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="work">The work.</param>
    /// <returns>The result.</returns>
    public async Task<T> WithDbAsync<T>(Func<BbsDbContext, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var scope = Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<BbsDbContext>());
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(EnvironmentName);

        // Development loads the developer's user-secrets; tests must never see real Discord settings, so these
        // are blanked first and a test's own settings below win over the blanks.
        foreach (var key in new[] { "Discord:ClientId", "Discord:ClientSecret", "Discord:BotToken", "SysOp:DiscordUserId" })
        {
            builder.UseSetting(key, string.Empty);
        }
        builder.UseSetting("ConnectionStrings:bbsdb", $"Data Source={DatabasePath}");
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<INotifier>(Notifier);
            services.AddSingleton<IDoorCatalog>(new TestCatalog());
            services.AddSingleton<TimeProvider>(Clock);
            this.services?.Invoke(services);
        });
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open by the OS is fine to leave in the temp folder.
        }
    }
}

/// <summary>
/// JSON helpers that add the anti-CSRF header the server requires.
/// </summary>
internal static class HttpExtensions
{
    /// <summary>
    /// Posts JSON with (or without) the anti-CSRF header.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="url">The URL.</param>
    /// <param name="body">The body, or <see langword="null"/>.</param>
    /// <param name="csrf">Whether to send the header.</param>
    /// <returns>The response.</returns>
    public static Task<HttpResponseMessage> PostJson(this HttpClient client, string url, object? body = null, bool csrf = true) =>
        client.Send(HttpMethod.Post, url, body, csrf);

    /// <summary>
    /// Sends a request with (or without) the anti-CSRF header.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="method">The method.</param>
    /// <param name="url">The URL.</param>
    /// <param name="body">The body, or <see langword="null"/>.</param>
    /// <param name="csrf">Whether to send the header.</param>
    /// <returns>The response.</returns>
    public static async Task<HttpResponseMessage> Send(this HttpClient client, HttpMethod method, string url, object? body = null, bool csrf = true)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (csrf)
        {
            request.Headers.Add("X-PIZON-BBS", "1");
        }

        return await client.SendAsync(request);
    }

    /// <summary>
    /// Reads a JSON response body.
    /// </summary>
    /// <typeparam name="T">The body type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="url">The URL.</param>
    /// <returns>The body.</returns>
    public static async Task<T> Get<T>(this HttpClient client, string url)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var response = await client.GetAsync(new Uri(url, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
