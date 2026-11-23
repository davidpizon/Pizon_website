using System.Globalization;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Services;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// Shared screen pieces: menu items, the callers-online text, prompts and the standard messages. Screen
/// layout itself lives in the compiled art (<c>Screens/Art</c>); these are the values bound into it.
/// </summary>
public static class Ui
{
    /// <summary>Clears the screen and resets colors.</summary>
    public const string Cls = "\u001b[0m\u001b[2J\u001b[H";

    /// <summary>The "press any key" prompt.</summary>
    public const string AnyKey = "|08-- press any key --|07";

    /// <summary>
    /// Translates pipe codes to ANSI.
    /// </summary>
    /// <param name="text">Text with pipe codes.</param>
    /// <returns>ANSI text.</returns>
    public static string P(string text) => PipeCodes.ToAnsi(text);

    /// <summary>
    /// Builds the callers-online text for the menu's bottom row.
    /// </summary>
    /// <param name="presence">Presence.</param>
    /// <returns>Pipe-code text, or an empty string when only this caller is online.</returns>
    public static string OnlineText(IPresence presence)
    {
        ArgumentNullException.ThrowIfNull(presence);
        return presence.Online > 1
            ? string.Create(CultureInfo.InvariantCulture, $"|08>> |11{presence.Online}|08 callers online · nodes busy: |07{string.Join(',', presence.Nodes)}|08 <<|07")
            : string.Empty;
    }

    /// <summary>
    /// Builds the "access denied" message for guests, as pipe-code text.
    /// </summary>
    /// <param name="session">The call.</param>
    /// <returns>Two lines of pipe-code text.</returns>
    public static string AccessDeniedText(BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"|09ACCESS DENIED - SECURITY LEVEL {SecurityLevels.User} REQUIRED. YOU ARE LEVEL {session.Level} ({PipeCodes.Escape(session.Handle)}).|07\n" +
            $"|08Sign in with Discord from the main menu: |14[D]|07");
    }

    /// <summary>
    /// Builds the "access denied" message for guests, for writing at the cursor.
    /// </summary>
    /// <param name="session">The call.</param>
    /// <returns>ANSI text.</returns>
    public static string AccessDenied(BbsSession session) => P("\r\n" + AccessDeniedText(session).Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n");

    /// <summary>
    /// Builds the message shown when an API call fails, as pipe-code text.
    /// </summary>
    /// <param name="result">The failed result.</param>
    /// <returns>One line of pipe-code text.</returns>
    public static string FailureText(ApiResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = result.Status switch
        {
            ApiStatus.Unauthorized => "|09ACCESS DENIED - SIGN IN FIRST.",
            ApiStatus.Forbidden => "|09ACCESS DENIED - THE SYSOP HAS SPOKEN.",
            ApiStatus.RateLimited => "|09SLOW DOWN, SPEED RACER. THE LINE NEEDS A MOMENT TO COOL OFF.",
            ApiStatus.Invalid => "|09REJECTED: " + PipeCodes.Escape((result.Error ?? "invalid input").ToUpperInvariant()),
            _ => "|09LINE NOISE - THE SERVER DID NOT ANSWER. TRY AGAIN.",
        };
        return text + "|07";
    }

    /// <summary>
    /// Builds the message shown when an API call fails, for writing at the cursor.
    /// </summary>
    /// <param name="result">The failed result.</param>
    /// <returns>ANSI text.</returns>
    public static string Failure(ApiResult result) => P("\r\n" + FailureText(result) + "\r\n");

    /// <summary>
    /// Shows the "press any key" prompt on the next line and waits.
    /// </summary>
    /// <param name="terminal">The terminal.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The key pressed.</returns>
    public static async Task<string> PressAnyKeyAsync(ITerminal terminal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        await terminal.WriteAsync(P("\r\n" + AnyKey), cancellationToken);
        return await terminal.ReadKeyAsync(cancellationToken);
    }

    /// <summary>
    /// Formats a hotkey menu item as a clickable hotspot, in pipe codes (for binding into art).
    /// </summary>
    /// <param name="key">The key (a single key; trusted text).</param>
    /// <param name="label">The label (pipe codes allowed; escape visitor text).</param>
    /// <returns>Pipe-code text.</returns>
    public static string ItemPipe(string key, string label) => $"|[{key}]|14[{key}]|07 {label}|07|]";

    /// <summary>
    /// Formats a timestamp for listings.
    /// </summary>
    /// <param name="when">The time.</param>
    /// <returns>For example <c>11/15/26 22:30</c>.</returns>
    public static string Stamp(DateTimeOffset when) => when.ToLocalTime().ToString("MM/dd/yy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a fake file size with thousands separators.
    /// </summary>
    /// <param name="bytes">The size.</param>
    /// <returns>The size text.</returns>
    public static string Size(long bytes) => bytes.ToString("N0", CultureInfo.InvariantCulture);
}
