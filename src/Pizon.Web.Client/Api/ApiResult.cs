namespace Pizon.Web.Client.Api;

/// <summary>
/// The outcome of an API call, mapped from the HTTP status so screens can show the right in-character message.
/// </summary>
public enum ApiStatus
{
    /// <summary>The call succeeded.</summary>
    Ok,

    /// <summary>The caller must sign in (HTTP 401).</summary>
    Unauthorized,

    /// <summary>The caller is not allowed, e.g. banned or not the SysOp (HTTP 403/404).</summary>
    Forbidden,

    /// <summary>Too many requests (HTTP 429).</summary>
    RateLimited,

    /// <summary>The input was rejected (HTTP 400/409/422); see the error text.</summary>
    Invalid,

    /// <summary>The server could not be reached or failed.</summary>
    Failed,
}

/// <summary>
/// A result without a value.
/// </summary>
/// <param name="Status">The outcome.</param>
/// <param name="Error">The server's error text for <see cref="ApiStatus.Invalid"/>, otherwise usually <see langword="null"/>.</param>
public record ApiResult(ApiStatus Status, string? Error = null)
{
    /// <summary>Gets a value indicating whether the call succeeded.</summary>
    public bool IsOk => Status == ApiStatus.Ok;
}

/// <summary>
/// A result carrying a value when successful.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="Status">The outcome.</param>
/// <param name="Value">The value when <see cref="ApiResult.IsOk"/>.</param>
/// <param name="Error">The server's error text, if any.</param>
public sealed record ApiResult<T>(ApiStatus Status, T? Value = default, string? Error = null) : ApiResult(Status, Error);
