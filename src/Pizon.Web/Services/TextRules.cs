using System.Text;

namespace Pizon.Web.Services;

/// <summary>
/// Validation for visitor text: control characters, length and the word blocklist.
/// </summary>
public static class TextRules
{
    /// <summary>
    /// Normalizes visitor text: unifies line endings, trims each line and the whole text, and drops
    /// control characters other than newline (so no ANSI escapes reach other callers' terminals).
    /// </summary>
    /// <param name="text">The raw text.</param>
    /// <param name="allowNewLines">Whether newlines are kept (messages) or turned into spaces (pages).</param>
    /// <returns>The cleaned text.</returns>
    public static string Clean(string? text, bool allowNewLines)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))
        {
            if (ch == '\n')
            {
                sb.Append(allowNewLines ? '\n' : ' ');
            }
            else if (ch == '\t')
            {
                sb.Append(' ');
            }
            else if (!char.IsControl(ch) && !IsBidiControl(ch))
            {
                sb.Append(ch);
            }
        }

        var lines = sb.ToString().Split('\n').Select(l => l.TrimEnd());
        return string.Join('\n', lines).Trim();
    }

    /// <summary>
    /// Checks for Unicode bidirectional override and isolate characters, which can disguise text.
    /// </summary>
    /// <param name="ch">The character.</param>
    /// <returns><see langword="true"/> for U+202A-U+202E and U+2066-U+2069.</returns>
    public static bool IsBidiControl(char ch) => ch is >= '‪' and <= '‮' or >= '⁦' and <= '⁩';

    /// <summary>
    /// Validates a message or page body.
    /// </summary>
    /// <param name="text">The cleaned text.</param>
    /// <param name="maxLength">The longest allowed length.</param>
    /// <param name="filter">The word filter.</param>
    /// <returns>A reason it was rejected, or <see langword="null"/> when it is fine.</returns>
    public static string? Validate(string text, int maxLength, WordFilter filter)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(filter);
        if (text.Length == 0)
        {
            return "message is empty";
        }

        if (text.Length > maxLength)
        {
            return $"message is longer than {maxLength} characters";
        }

        return filter.IsClean(text) ? null : "the word filter ate your message";
    }
}
