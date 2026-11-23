namespace Pizon.Web.Client.Terminal;

/// <summary>
/// The BBS's view of the screen and keyboard. The browser implementation is <see cref="JsTerminal"/>;
/// tests use a scripted fake.
/// </summary>
public interface ITerminal
{
    /// <summary>Writes ANSI text and waits until it has been painted at the current baud rate.</summary>
    /// <param name="ansi">ANSI text (translate pipe codes first).</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the text is on screen.</returns>
    Task WriteAsync(string ansi, CancellationToken cancellationToken = default);

    /// <summary>Applies ANSI text with an animated character-cell transition.</summary>
    /// <param name="effect">The effect.</param>
    /// <param name="ansi">The new content (start with a clear-screen for a new screen).</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the effect ends.</returns>
    Task TransitionAsync(Transition effect, string ansi, CancellationToken cancellationToken = default);

    /// <summary>Clears the screen.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when cleared.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Waits for the next key (printable keys as one character; others by name).</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The key.</returns>
    Task<string> ReadKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Waits for a key or until a timeout passes.</summary>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The key, or <see langword="null"/> on timeout.</returns>
    Task<string?> ReadKeyAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Drops keys typed ahead (for example while a screen was drawing).</summary>
    void DiscardPendingKeys();

    /// <summary>
    /// Waits for a duration while "move the mouse, tap or press a key to skip" is armed.
    /// </summary>
    /// <param name="duration">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns><see langword="true"/> when the caller skipped.</returns>
    Task<bool> WaitOrSkipAsync(TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>Waits without skip support (animation pacing).</summary>
    /// <param name="delay">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes after the delay.</returns>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>Plays an ANSImation from a URL.</summary>
    /// <param name="url">The animation URL, relative to the site root.</param>
    /// <param name="options">Playback options.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns><see langword="true"/> when the file existed and played.</returns>
    Task<bool> PlayAnimationAsync(string url, AnimationOptions options, CancellationToken cancellationToken = default);

    /// <summary>Loads an ANSI art file (CP437 or UTF-8).</summary>
    /// <param name="url">The file URL.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The art text, or <see langword="null"/> when missing.</returns>
    Task<string?> LoadArtAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>Shows the RIPscrip panel and waits for a button or key.</summary>
    /// <param name="url">The .rip URL.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The chosen key, or <see langword="null"/> when the file is missing.</returns>
    Task<string?> ShowRipAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>Turns line-input mode on or off (shows a phone keyboard).</summary>
    /// <param name="enabled">Whether a line is being typed.</param>
    /// <returns>A task that completes when applied.</returns>
    Task SetLineModeAsync(bool enabled);

    /// <summary>Applies baud and sound settings.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>A task that completes when applied.</returns>
    Task ApplySettingsAsync(TerminalSettings settings);

    /// <summary>Plays the dial-up handshake audio if sound is on.</summary>
    /// <returns>The audio length (zero when silent).</returns>
    Task<TimeSpan> PlayHandshakeAsync();

    /// <summary>Fades out any audio.</summary>
    /// <returns>A task that completes when requested.</returns>
    Task StopAudioAsync();

    /// <summary>Beeps (if sound is on).</summary>
    /// <returns>A task that completes when requested.</returns>
    Task BeepAsync();

    /// <summary>Opens an external URL in a new tab.</summary>
    /// <param name="url">An http(s) URL.</param>
    /// <returns>A task that completes when requested.</returns>
    Task OpenUrlAsync(Uri url);

    /// <summary>Gets the caller's local date.</summary>
    /// <returns>Today in the browser's time zone.</returns>
    Task<DateOnly> GetLocalDateAsync();

    /// <summary>
    /// Starts an animation playing in a box of the screen, behind whatever the page does: it doesn't wait in
    /// line with writes, and keys go to the page, not to the animation. Starting a slot that is already playing
    /// replaces it. Clearing the screen stops every slot. With reduced motion, only the first frame shows.
    /// </summary>
    /// <param name="name">The slot's name (one animation per name).</param>
    /// <param name="animation">The frames and the box.</param>
    /// <returns>A task that completes when it has started.</returns>
    Task StartSlotAsync(string name, SlotAnimation animation);

    /// <summary>Stops a slot's animation, leaving its current frame on screen.</summary>
    /// <param name="name">The slot's name.</param>
    /// <returns>A task that completes when it has stopped.</returns>
    Task StopSlotAsync(string name);
}

/// <summary>
/// An animation for a slot of the screen (see <see cref="ITerminal.StartSlotAsync"/>): finished ANSI frames,
/// each drawn from the top left of the box.
/// </summary>
/// <param name="Row">The box's top row (1-based).</param>
/// <param name="Column">The box's left column (1-based).</param>
/// <param name="Width">The box's width.</param>
/// <param name="Height">The box's height.</param>
/// <param name="Frames">The frames' ANSI, positioned within the box.</param>
/// <param name="Delays">How long each frame shows, in milliseconds.</param>
/// <param name="Loop">Whether it starts again after the last frame (otherwise it rests on the last).</param>
/// <param name="MaxDuration">How long it plays before resting on its first frame, if limited.</param>
public sealed record SlotAnimation(int Row, int Column, int Width, int Height, IReadOnlyList<string> Frames, IReadOnlyList<int> Delays, bool Loop = true, TimeSpan? MaxDuration = null);

/// <summary>
/// The character-cell transition effects (names match <c>transitions.js</c>).
/// </summary>
public enum Transition
{
    /// <summary>Draw immediately.</summary>
    None,

    /// <summary>Rows change from top to bottom.</summary>
    Wipe,

    /// <summary>Random cells change.</summary>
    Dissolve,

    /// <summary>Spray-paint reveal through ░▒▓ (the graffiti banner).</summary>
    Spray,

    /// <summary>Cells are drawn behind a moving block cursor (menu show).</summary>
    Draw,

    /// <summary>Cells are removed backwards behind the cursor (menu hide).</summary>
    Undraw,

    /// <summary>The old screen scrolls off the top.</summary>
    ScrollOff,
}

/// <summary>
/// ANSImation playback options.
/// </summary>
/// <param name="Row">Top row (0-based) for a boxed animation.</param>
/// <param name="Column">Left column (0-based) for a boxed animation.</param>
/// <param name="Width">Box width; 0 plays full screen.</param>
/// <param name="Height">Box height.</param>
/// <param name="Loop">Whether to loop until a key or <paramref name="MaxDuration"/>.</param>
/// <param name="MaxDuration">The longest playback.</param>
public sealed record AnimationOptions(int Row = 0, int Column = 0, int Width = 0, int Height = 0, bool Loop = false, TimeSpan? MaxDuration = null)
{
    /// <summary>Gets full-screen, play-once options.</summary>
    public static AnimationOptions FullScreen { get; } = new();
}
