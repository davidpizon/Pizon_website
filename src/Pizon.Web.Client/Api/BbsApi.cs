using System.Net;
using Pizon.Doors.Sdk;

namespace Pizon.Web.Client.Api;

/// <summary>
/// <see cref="IBbsApi"/> over <see cref="HttpClient"/>. Every request carries the <c>X-PIZON-BBS</c>
/// header the server requires on state-changing calls (a CSRF guard).
/// </summary>
public sealed class BbsApi : IBbsApi
{
    /// <summary>The anti-CSRF header name.</summary>
    public const string CsrfHeader = "X-PIZON-BBS";

    private readonly HttpClient http;

    /// <summary>
    /// Initializes a new instance of the <see cref="BbsApi"/> class.
    /// </summary>
    /// <param name="http">An HTTP client whose base address is the site root.</param>
    public BbsApi(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        this.http = http;
        if (!http.DefaultRequestHeaders.Contains(CsrfHeader))
        {
            http.DefaultRequestHeaders.Add(CsrfHeader, "1");
        }
    }

    /// <inheritdoc />
    public async Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var result = await GetAsync<MeResponse>("api/me", cancellationToken);
        return result.Value ?? MeResponse.Guest;
    }

    /// <inheritdoc />
    public Task<ApiResult> SetHandleAsync(string handle, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/me/handle", new SetHandleRequest(handle), cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult> LogoutAsync(CancellationToken cancellationToken = default) =>
        SendAsync<object?>(HttpMethod.Post, "auth/logout", null, cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult<IReadOnlyList<MessageDto>>> GetMessagesAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<MessageDto>("api/messages", cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult> PostMessageAsync(PostMessageRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/messages", request, cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult> PageSysopAsync(PageSysopRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/page-sysop", request, cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult<IReadOnlyList<CallerDto>>> GetRecentCallersAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<CallerDto>("api/callers/recent", cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult<IReadOnlyList<ScoreEntry>>> GetScoresAsync(string doorId, string day, CancellationToken cancellationToken = default) =>
        GetListAsync<ScoreEntry>($"api/scores/{Uri.EscapeDataString(doorId)}?day={Uri.EscapeDataString(day)}", cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult> SubmitScoreAsync(string doorId, ScoreSubmitRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"api/scores/{Uri.EscapeDataString(doorId)}", request, cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult<IReadOnlyList<PendingMessageDto>>> GetModerationQueueAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<PendingMessageDto>("api/sysop/queue", cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult> ModerateAsync(long messageId, bool approve, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"api/sysop/messages/{messageId}/moderate", new ModerateRequest(approve), cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult> DeleteMessageAsync(long messageId, CancellationToken cancellationToken = default) =>
        SendAsync<object?>(HttpMethod.Delete, $"api/sysop/messages/{messageId}", null, cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult<IReadOnlyList<SysopPageDto>>> GetPagesAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<SysopPageDto>("api/sysop/pages", cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult> MarkPageReadAsync(long pageId, CancellationToken cancellationToken = default) =>
        SendAsync<object?>(HttpMethod.Post, $"api/sysop/pages/{pageId}/read", null, cancellationToken);

    /// <inheritdoc />
    public Task<ApiResult> BanUserAsync(long userId, CancellationToken cancellationToken = default) =>
        SendAsync<object?>(HttpMethod.Post, $"api/sysop/users/{userId}/ban", null, cancellationToken);

    /// <summary>
    /// Maps an HTTP status code to an <see cref="ApiStatus"/>.
    /// </summary>
    /// <param name="code">The status code.</param>
    /// <returns>The API status.</returns>
    public static ApiStatus Map(HttpStatusCode code) => code switch
    {
        _ when (int)code is >= 200 and < 300 => ApiStatus.Ok,
        HttpStatusCode.Unauthorized => ApiStatus.Unauthorized,
        HttpStatusCode.Forbidden or HttpStatusCode.NotFound => ApiStatus.Forbidden,
        HttpStatusCode.TooManyRequests => ApiStatus.RateLimited,
        HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity => ApiStatus.Invalid,
        _ => ApiStatus.Failed,
    };

    private async Task<ApiResult<IReadOnlyList<T>>> GetListAsync<T>(string url, CancellationToken cancellationToken)
    {
        var result = await GetAsync<List<T>>(url, cancellationToken);
        return new ApiResult<IReadOnlyList<T>>(result.Status, result.Value ?? [], result.Error);
    }

    private async Task<ApiResult<T>> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(url, UriKind.Relative), cancellationToken);
            var status = Map(response.StatusCode);
            if (status != ApiStatus.Ok)
            {
                return new ApiResult<T>(status, default, await ReadErrorAsync(response, cancellationToken));
            }

            return new ApiResult<T>(ApiStatus.Ok, await response.Content.ReadFromJsonAsync<T>(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException)
        {
            return new ApiResult<T>(ApiStatus.Failed, default, ex.Message);
        }
    }

    private async Task<ApiResult> SendAsync<TBody>(HttpMethod method, string url, TBody body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await http.SendAsync(request, cancellationToken);
            var status = Map(response.StatusCode);
            return new ApiResult(status, status == ApiStatus.Ok ? null : await ReadErrorAsync(response, cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return new ApiResult(ApiStatus.Failed, ex.Message);
        }
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim().Trim('"');
    }
}
