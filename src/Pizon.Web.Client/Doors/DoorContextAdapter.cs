using Pizon.Doors.Sdk;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Services;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Doors;

/// <summary>
/// The <see cref="IDoorContext"/> a running door sees: the terminal, its own scores and its own storage, and
/// nothing else of the BBS.
/// </summary>
public sealed class DoorContextAdapter : IDoorContext
{
    private readonly ITerminal terminal;
    private readonly ContentStore content;
    private readonly string assetBase;

    /// <summary>
    /// Initializes a new instance of the <see cref="DoorContextAdapter"/> class.
    /// </summary>
    /// <param name="terminal">The terminal.</param>
    /// <param name="content">The content store (for door assets).</param>
    /// <param name="entry">The door's manifest entry.</param>
    /// <param name="caller">Who is playing.</param>
    /// <param name="today">The caller's local date.</param>
    /// <param name="scores">The door's score board.</param>
    /// <param name="storage">The door's storage.</param>
    public DoorContextAdapter(
        ITerminal terminal,
        ContentStore content,
        DoorManifestEntry entry,
        DoorCaller caller,
        DateOnly today,
        IDoorScores scores,
        IDoorStorage storage)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(entry);
        this.terminal = terminal;
        this.content = content;
        assetBase = entry.AssetBase;
        Caller = caller;
        Today = today;
        Scores = scores;
        Storage = storage;
    }

    /// <inheritdoc />
    public DoorCaller Caller { get; }

    /// <inheritdoc />
    public Random Random { get; } = new();

    /// <inheritdoc />
    public DateOnly Today { get; }

    /// <inheritdoc />
    public IDoorScores Scores { get; }

    /// <inheritdoc />
    public IDoorStorage Storage { get; }

    /// <summary>
    /// Resolves a door-relative asset path under the door's <c>_content</c> folder.
    /// </summary>
    /// <param name="assetBase">The door's asset base, for example <c>_content/Pizon.Doors.Oracle/</c>.</param>
    /// <param name="path">The asset path relative to the door's <c>wwwroot</c>.</param>
    /// <returns>The URL, or <see langword="null"/> when the path tries to leave the door's folder.</returns>
    public static string? ResolveAsset(string assetBase, string path)
    {
        ArgumentNullException.ThrowIfNull(assetBase);
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal) ||
            path.StartsWith('/') || path.Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        return assetBase + path;
    }

    /// <inheritdoc />
    public Task WriteAsync(string ansi, CancellationToken cancellationToken = default) => terminal.WriteAsync(ansi, cancellationToken);

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default) => terminal.ClearAsync(cancellationToken);

    /// <inheritdoc />
    public Task<string> ReadKeyAsync(CancellationToken cancellationToken = default) => terminal.ReadKeyAsync(cancellationToken);

    /// <inheritdoc />
    public Task<string?> ReadLineAsync(int maxLength, CancellationToken cancellationToken = default) =>
        LineEditor.ReadLineAsync(terminal, maxLength, string.Empty, cancellationToken);

    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) => terminal.DelayAsync(delay, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> PlayAnimationAsync(string path, CancellationToken cancellationToken = default) =>
        ResolveAsset(assetBase, path) is { } url && await terminal.PlayAnimationAsync(url, AnimationOptions.FullScreen, cancellationToken);

    /// <inheritdoc />
    public async Task<string?> LoadTextAsync(string path, CancellationToken cancellationToken = default) =>
        ResolveAsset(assetBase, path) is { } url ? await content.GetTextAsync(url, cancellationToken) : null;
}

/// <summary>
/// A door's score board over the server API. Guests' scores are shown but never submitted.
/// </summary>
/// <param name="api">The server API.</param>
/// <param name="doorId">The door id.</param>
/// <param name="caller">Who is playing.</param>
/// <param name="day">The caller's local date (yyyy-MM-dd).</param>
public sealed class ApiDoorScores(IBbsApi api, string doorId, DoorCaller caller, string day) : IDoorScores
{
    /// <inheritdoc />
    public async Task<bool> SubmitAsync(long score, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (!caller.IsAuthenticated)
        {
            return false;
        }

        var result = await api.SubmitScoreAsync(doorId, new ScoreSubmitRequest(score, day), cancellationToken);
        return result.IsOk;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScoreEntry>> TopAsync(CancellationToken cancellationToken = default)
    {
        var result = await api.GetScoresAsync(doorId, day, cancellationToken);
        return result.Value ?? [];
    }
}

/// <summary>
/// A door's storage: browser storage namespaced by door id, so doors can't read each other's data.
/// </summary>
/// <param name="storage">Browser storage.</param>
/// <param name="doorId">The door id.</param>
public sealed class NamespacedDoorStorage(IBrowserStorage storage, string doorId) : IDoorStorage
{
    /// <summary>
    /// Builds the browser storage key for a door key.
    /// </summary>
    /// <param name="doorId">The door id.</param>
    /// <param name="key">The door's own key.</param>
    /// <returns>The namespaced key.</returns>
    public static string KeyFor(string doorId, string key) => $"pizon.door.{doorId}.{key}";

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => storage.GetAsync(KeyFor(doorId, key));

    /// <inheritdoc />
    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) => storage.SetAsync(KeyFor(doorId, key), value);
}
