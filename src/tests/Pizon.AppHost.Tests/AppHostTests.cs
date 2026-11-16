using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pizon.AppHost.Tests;

/// <summary>
/// Smoke test: the Aspire AppHost starts the site, and the site answers its health check and home page.
/// </summary>
public sealed class AppHostTests
{
    /// <summary>The web resource becomes healthy and serves <c>/healthz</c> and <c>/</c>.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task WebStartsHealthy()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Pizon_AppHost>(timeout.Token);
        builder.Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("web", timeout.Token);

        using var client = app.CreateHttpClient("web");
        using var health = await client.GetAsync(new Uri("/healthz", UriKind.Relative), timeout.Token);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var home = await client.GetAsync(new Uri("/", UriKind.Relative), timeout.Token);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("PIZON", await home.Content.ReadAsStringAsync(timeout.Token), StringComparison.Ordinal);
    }
}
