namespace Pizon.Doors.Sdk;

/// <summary>
/// The only surface a door can reach. Doors never reference the BBS client, its session or other doors.
/// </summary>
public interface IDoorContext
{
    /// <summary>Gets who is playing.</summary>
    DoorCaller Caller { get; }

    /// <summary>Gets a random number source for the door.</summary>
    Random Random { get; }

    /// <summary>Gets today's date in the caller's local time.</summary>
    DateOnly Today { get; }

    /// <summary>Gets the shared high-score board for this door only.</summary>
    IDoorScores Scores { get; }

    /// <summary>Gets per-door storage in the caller's browser.</summary>
    IDoorStorage Storage { get; }

    /// <summary>
    /// Writes ANSI text (pipe codes are not translated; use <see cref="Text.PipeCodes"/> first).
    /// </summary>
    /// <param name="ansi">The ANSI text to write.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the text is queued for display.</returns>
    Task WriteAsync(string ansi, CancellationToken cancellationToken = default);

    /// <summary>Clears the screen.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the screen is cleared.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for the next key. Printable keys arrive as one-character strings; others by name
    /// (see <see cref="DoorKeys"/>).
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The key pressed.</returns>
    Task<string> ReadKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a line of text with echo and backspace support.
    /// </summary>
    /// <param name="maxLength">The maximum number of characters accepted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The text entered, or <see langword="null"/> when the caller pressed Escape.</returns>
    Task<string?> ReadLineAsync(int maxLength, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for a short time (for animation). Implementations may return early when the caller presses a key.
    /// </summary>
    /// <param name="delay">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes after the delay.</returns>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>
    /// Plays an ANSImation shipped in the door's <c>wwwroot</c>, if it exists.
    /// </summary>
    /// <param name="path">Path relative to the door's asset folder.</param>
    /// <param name="cancellationToken">Cancels playback.</param>
    /// <returns><see langword="true"/> when an animation was found and played.</returns>
    Task<bool> PlayAnimationAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a text asset shipped in the door's <c>wwwroot</c>.
    /// </summary>
    /// <param name="path">Path relative to the door's asset folder.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The file text, or <see langword="null"/> when it does not exist.</returns>
    Task<string?> LoadTextAsync(string path, CancellationToken cancellationToken = default);
}
