using System.Globalization;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Services;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// Shared screen pieces: headers, the status line, the callers-online line, prompts and paging.
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
    /// Builds a two-line section header.
    /// </summary>
    /// <param name="title">The section title.</param>
    /// <returns>ANSI text ending with a blank line.</returns>
    public static string Header(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var bar = new string('═', 78);
        return P($"|#033{bar}\r\n |11{TextWrap.Fit(title.ToUpperInvariant(), 60)}|#244{"PIZON BBS",17}\r\n|#033{bar}|07\r\n\r\n");
    }

    /// <summary>
    /// Builds the status header line (SysOp, node, date, mood, location, caller).
    /// </summary>
    /// <param name="session">The call.</param>
    /// <param name="presence">Presence, for the node number.</param>
    /// <param name="sysop">The SysOp's name.</param>
    /// <param name="mood">The SysOp's mood.</param>
    /// <param name="location">The SysOp's location.</param>
    /// <param name="now">The current time.</param>
    /// <returns>ANSI text for one or two lines.</returns>
    public static string StatusHeader(BbsSession session, IPresence presence, string sysop, string mood, string location, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(presence);
        var date = now.ToString("ddd MMM dd yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        return P(string.Create(
            CultureInfo.InvariantCulture,
            $"|08SysOp: |15{sysop}|08 · Node |11{presence.NodeNumber}|08 of |11{presence.Online}|08 · |07{date}\r\n" +
            $"|08Mood: |13{mood}|08 · Location: |13{location}|08 · Caller: |14{session.Handle}|08 (Lvl {session.Level})|07\r\n"));
    }

    /// <summary>
    /// Builds the callers-online line for row 25, or an empty string when only this caller is online.
    /// </summary>
    /// <param name="presence">Presence.</param>
    /// <returns>ANSI text that draws (or clears) row 25 without moving the cursor.</returns>
    public static string OnlineLine(IPresence presence)
    {
        ArgumentNullException.ThrowIfNull(presence);
        var body = presence.Online > 1
            ? P(string.Create(
                CultureInfo.InvariantCulture,
                $"|08>> |11{presence.Online}|08 callers online · nodes busy: |07{string.Join(',', presence.Nodes)}|08 <<|07"))
            : string.Empty;
        return Ansi.SaveCursor + Ansi.MoveTo(25, 1) + Ansi.EraseToEndOfLine + body + Ansi.RestoreCursor;
    }

    /// <summary>
    /// Builds the "access denied" message for guests.
    /// </summary>
    /// <param name="session">The call.</param>
    /// <returns>ANSI text.</returns>
    public static string AccessDenied(BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return P(string.Create(
            CultureInfo.InvariantCulture,
            $"\r\n|09ACCESS DENIED - SECURITY LEVEL {SecurityLevels.User} REQUIRED. YOU ARE LEVEL {session.Level} ({session.Handle}).|07\r\n" +
            $"|08Sign in with Discord from the main menu: |14[D]|07\r\n"));
    }

    /// <summary>
    /// Builds the message shown when an API call fails.
    /// </summary>
    /// <param name="result">The failed result.</param>
    /// <returns>ANSI text.</returns>
    public static string Failure(ApiResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = result.Status switch
        {
            ApiStatus.Unauthorized => "|09ACCESS DENIED - SIGN IN FIRST.",
            ApiStatus.Forbidden => "|09ACCESS DENIED - THE SYSOP HAS SPOKEN.",
            ApiStatus.RateLimited => "|09SLOW DOWN, SPEED RACER. THE LINE NEEDS A MOMENT TO COOL OFF.",
            ApiStatus.Invalid => "|09REJECTED: " + (result.Error ?? "invalid input").ToUpperInvariant(),
            _ => "|09LINE NOISE - THE SERVER DID NOT ANSWER. TRY AGAIN.",
        };
        return P("\r\n" + text + "|07\r\n");
    }

    /// <summary>
    /// Shows the "press any key" prompt and waits.
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
    /// Shows lines a page at a time with a "-- more --" prompt.
    /// </summary>
    /// <param name="terminal">The terminal.</param>
    /// <param name="lines">ANSI lines (without line endings).</param>
    /// <param name="pageSize">Lines per page.</param>
    /// <param name="cancellationToken">Cancels paging.</param>
    /// <returns><see langword="true"/> when all lines were shown; <see langword="false"/> when the caller quit early.</returns>
    public static async Task<bool> PageAsync(ITerminal terminal, IReadOnlyList<string> lines, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(lines);
        for (var i = 0; i < lines.Count; i++)
        {
            await terminal.WriteAsync(lines[i] + "\r\n", cancellationToken);
            if ((i + 1) % pageSize == 0 && i + 1 < lines.Count)
            {
                await terminal.WriteAsync(P("|08-- more -- |14[Enter]|08 next page  |14[Q]|08 stop|07"), cancellationToken);
                var key = await terminal.ReadKeyAsync(cancellationToken);
                await terminal.WriteAsync("\r" + Ansi.EraseToEndOfLine, cancellationToken);
                if (DoorKeys.Is(key, 'Q') || key == DoorKeys.Escape)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Formats a hotkey menu item as a clickable hotspot.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="label">The label.</param>
    /// <param name="keyLabel">The key as shown (defaults to <paramref name="key"/>).</param>
    /// <returns>ANSI text.</returns>
    public static string Item(string key, string label, string? keyLabel = null) =>
        Ansi.Hotspot(key, P($"|14[{keyLabel ?? key}]|07 {label}"));

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
