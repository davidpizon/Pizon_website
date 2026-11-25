using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Pizon.Web.Services.Discord;

/// <summary>
/// Reads channel messages with the Discord REST API as the feeds' bot.
/// </summary>
/// <param name="http">The HTTP client (its base address is <see cref="ApiBase"/>).</param>
/// <param name="options">The Discord feed options (for the bot token).</param>
public sealed class DiscordClient(HttpClient http, IOptions<DiscordFeedOptions> options)
{
    /// <summary>The most messages Discord returns per request.</summary>
    public const int PageSize = 100;

    /// <summary>The Discord REST API root.</summary>
    public static readonly Uri ApiBase = new("https://discord.com/api/v10/");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Gets messages from a channel.
    /// </summary>
    /// <param name="channelId">The channel id.</param>
    /// <param name="after">Only messages newer than this message id (oldest first), or <see langword="null"/> for the newest.</param>
    /// <param name="limit">How many, 1 to <see cref="PageSize"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The messages, in the order Discord sent them.</returns>
    /// <exception cref="DiscordUnavailableException">Discord could not be read.</exception>
    public async Task<IReadOnlyList<DiscordMessage>> GetMessagesAsync(string channelId, string? after, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, PageSize);
        var url = string.Create(CultureInfo.InvariantCulture, $"channels/{Uri.EscapeDataString(channelId)}/messages?limit={limit}");
        if (after is not null)
        {
            url += "&after=" + Uri.EscapeDataString(after);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bot", options.Value.BotToken);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var reason = ErrorMessage(await response.Content.ReadAsStringAsync(cancellationToken));
                throw new DiscordUnavailableException(string.Create(CultureInfo.InvariantCulture, $"Discord answered {(int)response.StatusCode} for channel {channelId}: {reason}"));
            }

            return await response.Content.ReadFromJsonAsync<List<DiscordMessage>>(Json, cancellationToken) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or FormatException ||
            (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new DiscordUnavailableException("Discord could not be reached", ex);
        }
    }

    /// <summary>
    /// Reads Discord's reason from an error body, such as <c>{"message": "Missing Access", "code": 50001}</c>.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <returns>The message (and code), or <c>no reason given</c>.</returns>
    public static string ErrorMessage(string? body)
    {
        try
        {
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number
                    ? string.Create(CultureInfo.InvariantCulture, $"{message.GetString()} (code {code.GetRawText()})")
                    : message.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Not JSON: fall through.
        }

        return "no reason given";
    }
}
