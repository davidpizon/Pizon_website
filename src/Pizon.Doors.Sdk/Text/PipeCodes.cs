using System.Globalization;
using System.Text;

namespace Pizon.Doors.Sdk.Text;

/// <summary>
/// Converts PIZON BBS pipe codes into ANSI escape sequences.
/// </summary>
/// <remarks>
/// Supported codes:
/// <list type="bullet">
/// <item><c>|00</c>-<c>|15</c>: foreground color 0-15 (WWIV/Renegade convention).</item>
/// <item><c>|16</c>-<c>|23</c>: background color 0-7.</item>
/// <item><c>|#NNN</c> / <c>|%NNN</c>: 256-color foreground / background (three digits).</item>
/// <item><c>|[K]</c> … <c>|]</c>: a clickable hotspot that sends key <c>K</c>.</item>
/// <item><c>|{https://…}</c> … <c>|]</c>: a hyperlink to an external URL.</item>
/// <item><c>|!</c>: reset attributes. <c>|CL</c>: clear screen. <c>||</c>: a literal pipe.</item>
/// </list>
/// Anything else after a pipe is left untouched.
/// </remarks>
public static class PipeCodes
{
    /// <summary>
    /// Converts text containing pipe codes into ANSI.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <returns>The text with every recognized pipe code replaced by its ANSI sequence.</returns>
    public static string ToAnsi(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new StringBuilder(text.Length + 32);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c != '|' || i + 1 >= text.Length)
            {
                sb.Append(c);
                i++;
                continue;
            }

            i += Translate(text, i, sb);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Makes untrusted text (visitor messages, handles) safe to embed in pipe-code text: control
    /// characters (including ESC) are dropped and every pipe is doubled, so it can't change colors,
    /// create hotspots or inject links.
    /// </summary>
    /// <param name="untrusted">The untrusted text.</param>
    /// <returns>Text that renders literally.</returns>
    public static string Escape(string untrusted)
    {
        ArgumentNullException.ThrowIfNull(untrusted);
        var sb = new StringBuilder(untrusted.Length);
        foreach (var c in untrusted)
        {
            if (c == '|')
            {
                sb.Append("||");
            }
            else if (!char.IsControl(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Removes pipe codes and ANSI escape sequences, leaving only the visible characters.
    /// </summary>
    /// <param name="text">Text with pipe codes and/or ANSI sequences.</param>
    /// <returns>The plain visible text.</returns>
    public static string StripToPlain(string text) => StripAnsi(ToAnsi(text));

    /// <summary>
    /// Gets the number of visible characters in a string with pipe codes or ANSI sequences.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <returns>The visible length.</returns>
    public static int VisibleLength(string text) => StripToPlain(text).Length;

    /// <summary>
    /// Removes ANSI CSI and OSC sequences from text.
    /// </summary>
    /// <param name="ansi">Text that may contain escape sequences.</param>
    /// <returns>The text without escape sequences.</returns>
    public static string StripAnsi(string ansi)
    {
        ArgumentNullException.ThrowIfNull(ansi);
        var sb = new StringBuilder(ansi.Length);
        var i = 0;
        while (i < ansi.Length)
        {
            if (ansi[i] != '\u001b' || i + 1 >= ansi.Length)
            {
                sb.Append(ansi[i]);
                i++;
                continue;
            }

            if (ansi[i + 1] == '[')
            {
                i += 2;
                while (i < ansi.Length && ansi[i] is < '@' or > '~')
                {
                    i++;
                }

                i++;
            }
            else if (ansi[i + 1] == ']')
            {
                var end = ansi.IndexOf("\u001b\\", i + 2, StringComparison.Ordinal);
                i = end < 0 ? ansi.Length : end + 2;
            }
            else
            {
                i += 2;
            }
        }

        return sb.ToString();
    }

    private static int Translate(string text, int start, StringBuilder sb)
    {
        var next = text[start + 1];
        switch (next)
        {
            case '|':
                sb.Append('|');
                return 2;
            case '!':
                sb.Append(Ansi.Reset);
                return 2;
            case ']':
                sb.Append(Ansi.LinkEnd());
                return 2;
            case '#' or '%':
                return TranslateExtendedColor(text, start, next == '#', sb);
            case '[':
                return TranslateBracket(text, start, ']', key => Ansi.HotspotStart(key), sb);
            case '{':
                return TranslateBracket(text, start, '}', TryLink, sb);
            case 'C' when start + 2 < text.Length && text[start + 2] == 'L':
                sb.Append(Ansi.ClearScreen);
                return 3;
            default:
                return TranslateClassicColor(text, start, sb);
        }
    }

    private static int TranslateClassicColor(string text, int start, StringBuilder sb)
    {
        if (start + 2 < text.Length && char.IsAsciiDigit(text[start + 1]) && char.IsAsciiDigit(text[start + 2]))
        {
            var code = ((text[start + 1] - '0') * 10) + (text[start + 2] - '0');
            if (code <= 15)
            {
                sb.Append(Ansi.Fg(code));
                return 3;
            }

            if (code <= 23)
            {
                sb.Append(Ansi.Bg(code - 16));
                return 3;
            }
        }

        sb.Append('|');
        return 1;
    }

    private static int TranslateExtendedColor(string text, int start, bool foreground, StringBuilder sb)
    {
        if (start + 4 < text.Length
            && int.TryParse(text.AsSpan(start + 2, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && index < Palette256.Count)
        {
            sb.Append(foreground ? Ansi.Fg(index) : Ansi.Bg(index));
            return 5;
        }

        sb.Append('|');
        return 1;
    }

    private static int TranslateBracket(string text, int start, char close, Func<string, string?> emit, StringBuilder sb)
    {
        var end = text.IndexOf(close, start + 2);
        if (end > start + 2)
        {
            var produced = emit(text[(start + 2)..end]);
            if (produced is not null)
            {
                sb.Append(produced);
                return end - start + 1;
            }
        }

        sb.Append('|');
        return 1;
    }

    private static string? TryLink(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? Ansi.LinkStart(uri)
            : null;
}
