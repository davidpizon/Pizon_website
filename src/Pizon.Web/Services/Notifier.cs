using Microsoft.Extensions.Options;

namespace Pizon.Web.Services;

/// <summary>
/// Options for SysOp notifications (<c>Notify</c> section).
/// </summary>
public sealed class NotifyOptions
{
    /// <summary>Gets or sets the webhook URL: an ntfy topic URL or a Discord webhook URL. Empty logs only.</summary>
    public Uri? WebhookUrl { get; set; }
}

/// <summary>
/// Sends the SysOp a push notification.
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Sends a notification. Never throws: failures are logged and never block the visitor.
    /// </summary>
    /// <param name="title">A short title.</param>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes when sent (or given up on).</returns>
    Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Posts notifications to one webhook: Discord webhooks get <c>{"content": ...}</c> JSON, anything else is
/// treated as an ntfy topic (plain-text body, <c>Title</c> header).
/// </summary>
/// <param name="http">The HTTP client.</param>
/// <param name="options">The notification options.</param>
/// <param name="logger">The logger.</param>
public sealed partial class WebhookNotifier(HttpClient http, IOptions<NotifyOptions> options, ILogger<WebhookNotifier> logger) : INotifier
{
    /// <summary>
    /// Checks whether a URL is a Discord webhook.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <returns><see langword="true"/> for <c>discord.com/api/webhooks/...</c> (and the older discordapp.com).</returns>
    public static bool IsDiscordWebhook(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.Host is "discord.com" or "discordapp.com" && url.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var url = options.Value.WebhookUrl;
        if (url is null)
        {
            LogOnly(logger, title, message);
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (IsDiscordWebhook(url))
            {
                request.Content = JsonContent.Create(new { content = $"**{title}**\n{message}" });
            }
            else
            {
                request.Content = new StringContent(message);
                request.Headers.Add("Title", title);
                request.Headers.Add("Tags", "telephone_receiver");
            }

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, (int)response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            LogError(logger, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogError(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification (no webhook configured): {Title}: {Message}")]
    private static partial void LogOnly(ILogger logger, string title, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification webhook answered {Status}")]
    private static partial void LogFailed(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification webhook failed")]
    private static partial void LogError(ILogger logger, Exception exception);
}
