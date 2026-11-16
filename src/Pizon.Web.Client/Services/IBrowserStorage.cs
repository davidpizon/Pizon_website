namespace Pizon.Web.Client.Services;

/// <summary>
/// Per-browser key/value storage (localStorage). Reads return <see langword="null"/> and writes are
/// ignored when storage is unavailable, so nothing important may depend on it.
/// </summary>
public interface IBrowserStorage
{
    /// <summary>Reads a value.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    Task<string?> GetAsync(string key);

    /// <summary>Writes a value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A task that completes when written.</returns>
    Task SetAsync(string key, string value);
}
