namespace Pizon.Doors.Sdk.Text;

/// <summary>
/// Plain-text helpers for fixed-width terminal layout.
/// </summary>
public static class TextWrap
{
    /// <summary>
    /// Word-wraps plain text to a maximum width. Words longer than the width are split.
    /// </summary>
    /// <param name="text">The plain text (no escape sequences).</param>
    /// <param name="width">The maximum line width; must be at least 1.</param>
    /// <returns>The wrapped lines; an empty input yields one empty line.</returns>
    public static IReadOnlyList<string> Wrap(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var current = string.Empty;
            foreach (var rawWord in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var word = rawWord;
                while (word.Length > width)
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current);
                        current = string.Empty;
                    }

                    lines.Add(word[..width]);
                    word = word[width..];
                }

                if (current.Length == 0)
                {
                    current = word;
                }
                else if (current.Length + 1 + word.Length <= width)
                {
                    current += " " + word;
                }
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }

            lines.Add(current);
        }

        return lines;
    }

    /// <summary>
    /// Pads or truncates plain text to exactly <paramref name="width"/> characters.
    /// </summary>
    /// <param name="text">The plain text.</param>
    /// <param name="width">The target width.</param>
    /// <returns>The fitted text.</returns>
    public static string Fit(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        return text.Length >= width ? text[..width] : text.PadRight(width);
    }

    /// <summary>
    /// Centers plain text within a width, truncating when it is too long.
    /// </summary>
    /// <param name="text">The plain text.</param>
    /// <param name="width">The target width.</param>
    /// <returns>The centered text, exactly <paramref name="width"/> characters long.</returns>
    public static string Center(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        if (text.Length >= width)
        {
            return text[..width];
        }

        var left = (width - text.Length) / 2;
        return new string(' ', left) + text + new string(' ', width - text.Length - left);
    }
}
