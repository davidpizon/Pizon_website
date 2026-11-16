namespace Pizon.Doors.Sdk;

/// <summary>
/// Small key/value storage in the caller's browser, namespaced to one door.
/// </summary>
public interface IDoorStorage
{
    /// <summary>Reads a stored value.</summary>
    /// <param name="key">The key within this door's namespace.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The value, or <see langword="null"/> when absent or storage is unavailable.</returns>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores a value. Failures (private mode, blocked storage) are ignored.</summary>
    /// <param name="key">The key within this door's namespace.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the value is stored.</returns>
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}
