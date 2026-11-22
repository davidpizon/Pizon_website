using System.Text;

namespace Pizon.Doors.Sdk.Text;

/// <summary>
/// Where text sits inside a field that is wider than the text.
/// </summary>
public enum TextAlign
{
    /// <summary>Against the left edge.</summary>
    Left,

    /// <summary>Against the right edge.</summary>
    Right,

    /// <summary>In the middle (an odd leftover space goes on the right).</summary>
    Center,
}

/// <summary>
/// Lays out text with pipe codes (see <see cref="PipeCodes"/>) inside a fixed-width box. Widths count only
/// visible characters. Words wrap at spaces; a word longer than the box is split. A line break never loses
/// the color in effect, and a hotspot or link that is broken across lines is closed at the end of one line
/// and reopened at the start of the next, so every piece stays clickable. A hotspot or link that fits on a
/// line is never split.
/// </summary>
public static class TextLayout
{
    /// <summary>The marker added when text is cut short.</summary>
    public const string Ellipsis = "...";

    /// <summary>
    /// Word-wraps pipe-code text to a width. Explicit line breaks (<c>\n</c>, <c>\r\n</c>) are kept.
    /// </summary>
    /// <param name="pipeText">The text (visitor text must already be escaped with <see cref="PipeCodes.Escape"/>).</param>
    /// <param name="width">The width in visible characters; at least 1.</param>
    /// <returns>The lines, each with a visible length of at most <paramref name="width"/>. Empty text gives one empty line.</returns>
    public static IReadOnlyList<string> Wrap(string pipeText, int width)
    {
        ArgumentNullException.ThrowIfNull(pipeText);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        var writer = new LineWriter(width);
        foreach (var paragraph in pipeText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            foreach (var unit in Units(Tokenize(paragraph)))
            {
                writer.AddUnit(unit);
            }

            writer.EndParagraph();
        }

        return writer.Lines;
    }

    /// <summary>
    /// Lays out text in a box: wraps it, then keeps at most <paramref name="height"/> lines. When lines are
    /// dropped, the last kept line ends with <see cref="Ellipsis"/>.
    /// </summary>
    /// <param name="pipeText">The text.</param>
    /// <param name="width">The box width; at least 1.</param>
    /// <param name="height">The box height; at least 1.</param>
    /// <returns>The lines (between 1 and <paramref name="height"/>).</returns>
    public static IReadOnlyList<string> Box(string pipeText, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        var lines = Wrap(pipeText, width);
        if (lines.Count <= height)
        {
            return lines;
        }

        var kept = lines.Take(height).ToList();
        kept[^1] = Shorten(kept[^1], width);
        return kept;
    }

    /// <summary>
    /// Fits text on one line: text that is too long is cut at the last whole visible character that leaves
    /// room for <see cref="Ellipsis"/>. Explicit line breaks become spaces.
    /// </summary>
    /// <param name="pipeText">The text.</param>
    /// <param name="width">The width; at least 1.</param>
    /// <returns>The text, with a visible length of at most <paramref name="width"/>.</returns>
    public static string Fit(string pipeText, int width)
    {
        ArgumentNullException.ThrowIfNull(pipeText);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        var single = pipeText.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ');
        return PipeCodes.VisibleLength(single) <= width ? single : Shorten(single, width);
    }

    /// <summary>
    /// Works out the spaces to put before and after text of a given visible length.
    /// </summary>
    /// <param name="visible">The text's visible length.</param>
    /// <param name="width">The field width.</param>
    /// <param name="align">The alignment.</param>
    /// <returns>The number of spaces before and after.</returns>
    public static (int Before, int After) Padding(int visible, int width, TextAlign align)
    {
        var room = Math.Max(0, width - visible);
        var before = align switch
        {
            TextAlign.Right => room,
            TextAlign.Center => room / 2,
            _ => 0,
        };
        return (before, room - before);
    }

    /// <summary>
    /// Cuts a line so that it plus <see cref="Ellipsis"/> fits the width (just the dots when the width is tiny).
    /// </summary>
    private static string Shorten(string line, int width)
    {
        var keep = width - Ellipsis.Length;
        if (keep <= 0)
        {
            return Ellipsis[..width];
        }

        var sb = new StringBuilder();
        var visible = 0;
        var inLink = false;
        foreach (var token in Tokenize(line))
        {
            if (token.IsText)
            {
                if (visible == keep)
                {
                    break;
                }

                visible++;
            }
            else if (token.Kind == CodeKind.LinkOpen)
            {
                inLink = true;
            }
            else if (token.Kind == CodeKind.LinkClose)
            {
                inLink = false;
            }

            sb.Append(token.Raw);
        }

        var cut = sb.ToString().TrimEnd(' ');
        return cut + (inLink ? "|]" : string.Empty) + Ellipsis;
    }

    /// <summary>
    /// Splits text into tokens: single visible characters and pipe codes (with the same rules as
    /// <see cref="PipeCodes.ToAnsi"/>; anything it would print literally is a visible character here).
    /// </summary>
    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '|' && i + 1 < text.Length && text[i + 1] == '|')
            {
                tokens.Add(new Token("||", CodeKind.Literal));
                i += 2;
                continue;
            }

            var kind = CodeKind.None;
            var length = text[i] == '|' ? CodeLength(text, i, out kind) : 0;
            if (length == 0)
            {
                tokens.Add(new Token(text[i] == '\t' ? " " : text[i].ToString(), CodeKind.None));
                i++;
                continue;
            }

            tokens.Add(new Token(text.Substring(i, length), kind));
            i += length;
        }

        return tokens;
    }

    /// <summary>
    /// Measures a pipe code starting at <paramref name="start"/>.
    /// </summary>
    /// <returns>Its length, or 0 when the pipe is printed literally.</returns>
    private static int CodeLength(string text, int start, out CodeKind kind)
    {
        kind = CodeKind.None;
        if (start + 1 >= text.Length)
        {
            return 0;
        }

        var next = text[start + 1];
        switch (next)
        {
            case '!':
                kind = CodeKind.Reset;
                return 2;
            case ']':
                kind = CodeKind.LinkClose;
                return 2;
            case '#' or '%':
                if (start + 4 < text.Length && int.TryParse(text.AsSpan(start + 2, 3), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) && index < Palette256.Count)
                {
                    kind = next == '#' ? CodeKind.Foreground : CodeKind.Background;
                    return 5;
                }

                return 0;
            case '[' or '{':
                var close = text.IndexOf(next == '[' ? ']' : '}', start + 2);
                if (close > start + 2 && (next == '[' || IsWebLink(text[(start + 2)..close])))
                {
                    kind = CodeKind.LinkOpen;
                    return close - start + 1;
                }

                return 0;
            case 'C' when start + 2 < text.Length && text[start + 2] == 'L':
                kind = CodeKind.Reset;
                return 3;
            default:
                if (start + 2 < text.Length && char.IsAsciiDigit(next) && char.IsAsciiDigit(text[start + 2]))
                {
                    var code = ((next - '0') * 10) + (text[start + 2] - '0');
                    if (code <= 23)
                    {
                        kind = code <= 15 ? CodeKind.Foreground : CodeKind.Background;
                        return 3;
                    }
                }

                return 0;
        }
    }

    /// <summary>Checks that a link target is an absolute http or https address.</summary>
    private static bool IsWebLink(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>
    /// Groups tokens into units separated by single spaces. Spaces inside a hotspot or link don't split it.
    /// </summary>
    private static IEnumerable<List<Token>> Units(List<Token> tokens)
    {
        var unit = new List<Token>();
        var inLink = false;
        foreach (var token in tokens)
        {
            if (token.Kind == CodeKind.None && token.Raw == " " && !inLink)
            {
                yield return unit;
                unit = [];
                continue;
            }

            inLink = token.Kind switch
            {
                CodeKind.LinkOpen => true,
                CodeKind.LinkClose => false,
                _ => inLink,
            };
            unit.Add(token);
        }

        yield return unit;
    }

    /// <summary>The kinds of pipe code.</summary>
    private enum CodeKind
    {
        None,
        Literal,
        Foreground,
        Background,
        Reset,
        LinkOpen,
        LinkClose,
    }

    /// <summary>A visible character (<see cref="CodeKind.None"/> or <see cref="CodeKind.Literal"/>) or a pipe code.</summary>
    private readonly record struct Token(string Raw, CodeKind Kind)
    {
        /// <summary>Gets a value indicating whether the token takes one visible cell.</summary>
        public bool IsText => Kind is CodeKind.None or CodeKind.Literal;
    }

    /// <summary>
    /// Builds wrapped lines, tracking the colors and open link so each new line can restore them.
    /// </summary>
    private sealed class LineWriter(int width)
    {
        private readonly StringBuilder line = new();
        private string? foreground;
        private string? background;
        private string? link;
        private int visible;
        private bool pendingSpace;

        /// <summary>Gets the finished lines.</summary>
        public List<string> Lines { get; } = [];

        /// <summary>Adds one space-separated unit, wrapping before it when it doesn't fit.</summary>
        public void AddUnit(List<Token> unit)
        {
            var size = unit.Count(t => t.IsText);
            var gap = pendingSpace && visible > 0 ? 1 : 0;
            if (size > 0 && visible > 0 && visible + gap + size > width)
            {
                Break();
                gap = 0;
            }

            if (gap == 1 && size > 0)
            {
                line.Append(' ');
                visible++;
            }

            foreach (var token in unit)
            {
                if (token.IsText && visible == width)
                {
                    Break();
                }

                Write(token);
            }

            pendingSpace = true;
        }

        /// <summary>Ends a paragraph (an explicit line break).</summary>
        public void EndParagraph()
        {
            Finish();
            pendingSpace = false;
        }

        private void Write(Token token)
        {
            line.Append(token.Raw);
            switch (token.Kind)
            {
                case CodeKind.None or CodeKind.Literal:
                    visible++;
                    break;
                case CodeKind.Foreground:
                    foreground = token.Raw;
                    break;
                case CodeKind.Background:
                    background = token.Raw;
                    break;
                case CodeKind.Reset:
                    foreground = background = null;
                    break;
                case CodeKind.LinkOpen:
                    link = token.Raw;
                    break;
                default:
                    link = null;
                    break;
            }
        }

        private void Break()
        {
            Finish();
            line.Append(foreground).Append(background).Append(link);
        }

        private void Finish()
        {
            var trimmed = line.ToString().TrimEnd(' ');
            Lines.Add(link is null ? trimmed : trimmed + "|]");
            line.Clear();
            visible = 0;
        }
    }
}
