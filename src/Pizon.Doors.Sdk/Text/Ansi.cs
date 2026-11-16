using System.Globalization;

namespace Pizon.Doors.Sdk.Text;

/// <summary>
/// Builders for the ANSI escape sequences the PIZON BBS terminal understands.
/// </summary>
public static class Ansi
{
    /// <summary>The escape character.</summary>
    public const string Esc = "\u001b";

    /// <summary>The control sequence introducer (<c>ESC [</c>).</summary>
    public const string Csi = Esc + "[";

    /// <summary>Resets all colors and attributes.</summary>
    public const string Reset = Csi + "0m";

    /// <summary>Clears the screen and homes the cursor.</summary>
    public const string ClearScreen = Csi + "2J" + Csi + "H";

    /// <summary>Moves the cursor to the top-left corner.</summary>
    public const string Home = Csi + "H";

    /// <summary>Erases from the cursor to the end of the line.</summary>
    public const string EraseToEndOfLine = Csi + "K";

    /// <summary>Saves the cursor position.</summary>
    public const string SaveCursor = Csi + "s";

    /// <summary>Restores the cursor position saved by <see cref="SaveCursor"/>.</summary>
    public const string RestoreCursor = Csi + "u";

    /// <summary>Carriage return plus line feed.</summary>
    public const string NewLine = "\r\n";

    /// <summary>
    /// Moves the cursor to a 1-based row and column.
    /// </summary>
    /// <param name="row">The 1-based row (1-25).</param>
    /// <param name="column">The 1-based column (1-80).</param>
    /// <returns>The cursor-position sequence.</returns>
    public static string MoveTo(int row, int column) =>
        string.Create(CultureInfo.InvariantCulture, $"{Csi}{row};{column}H");

    /// <summary>
    /// Sets the foreground to a 256-color palette index.
    /// </summary>
    /// <param name="index">The palette index, 0-255.</param>
    /// <returns>The SGR sequence.</returns>
    public static string Fg(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{Csi}38;5;{CheckIndex(index)}m");

    /// <summary>
    /// Sets the background to a 256-color palette index.
    /// </summary>
    /// <param name="index">The palette index, 0-255.</param>
    /// <returns>The SGR sequence.</returns>
    public static string Bg(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{Csi}48;5;{CheckIndex(index)}m");

    /// <summary>
    /// Starts a clickable hotspot that sends <paramref name="key"/> when clicked (OSC 8 with a <c>key:</c> target).
    /// </summary>
    /// <param name="key">The key the hotspot sends, for example <c>1</c> or <c>Enter</c>.</param>
    /// <returns>The OSC 8 opening sequence.</returns>
    public static string HotspotStart(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return $"{Esc}]8;;key:{key}{Esc}\\";
    }

    /// <summary>
    /// Starts a hyperlink to an external URL (OSC 8). The terminal renders it as a link opening in a new tab.
    /// </summary>
    /// <param name="url">An absolute http or https URL.</param>
    /// <returns>The OSC 8 opening sequence.</returns>
    public static string LinkStart(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return $"{Esc}]8;;{url.AbsoluteUri}{Esc}\\";
    }

    /// <summary>Ends the current hotspot or hyperlink.</summary>
    /// <returns>The OSC 8 closing sequence.</returns>
    public static string LinkEnd() => $"{Esc}]8;;{Esc}\\";

    /// <summary>
    /// Wraps a label in a hotspot bound to a key.
    /// </summary>
    /// <param name="key">The key the hotspot sends.</param>
    /// <param name="label">The visible text (may contain ANSI color codes).</param>
    /// <returns>The complete hotspot text.</returns>
    public static string Hotspot(string key, string label) => HotspotStart(key) + label + LinkEnd();

    private static int CheckIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Palette256.Count);
        return index;
    }
}
