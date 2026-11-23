using System.Text;
using Pizon.Doors.Sdk.Text;

namespace Pizon.Web.Client.Screens.Rendering;

/// <summary>
/// The text typed into an input slot (<c>@?NAME@</c>), edited the classic BBS way: characters go on the end
/// and Backspace takes the last one off. A one-line slot stops at its length; a taller slot word-wraps as
/// you type and keeps Enter's line breaks. One cell of each line is kept free for the cursor. Every edit
/// returns positioned ANSI that turns what is on screen into the new text, sending only what changed, so
/// typing stays quick at 2400 baud.
/// </summary>
public sealed class TextBox
{
    private readonly FieldSlot slot;
    private readonly int maxLength;
    private readonly string colors;
    private string text = string.Empty;
    private IReadOnlyList<string> lines = [string.Empty];

    /// <summary>
    /// Initializes a new instance of the <see cref="TextBox"/> class.
    /// </summary>
    /// <param name="slot">The input slot.</param>
    /// <param name="maxLength">The most characters accepted, line breaks included.</param>
    public TextBox(FieldSlot slot, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);
        this.slot = slot;
        this.maxLength = maxLength;
        colors = Ansi.Fg(slot.Foreground) + Ansi.Bg(slot.Background);
    }

    /// <summary>Gets the slot.</summary>
    public FieldSlot Slot => slot;

    /// <summary>Gets the text, with <c>\n</c> between the lines Enter started.</summary>
    public string Text => text;

    /// <summary>Gets the text as it is laid out in the slot: one entry per screen line.</summary>
    public IReadOnlyList<string> Lines => lines;

    /// <summary>Gets a value indicating whether the slot is taller than one line (word-wrapped, Enter breaks lines).</summary>
    public bool Multiline => slot.Height > 1;

    /// <summary>Gets the characters that fit on one line: the slot's width less one cell for the cursor.</summary>
    public int LineWidth => Math.Max(1, slot.Width - 1);

    /// <summary>Gets a value indicating whether the text is empty or its last line is blank (Enter twice ends a message).</summary>
    public bool EndsWithBlankLine => text.Length == 0 || text.EndsWith('\n');

    /// <summary>Gets ANSI that puts the cursor just after the text, in the slot's colors.</summary>
    public string Caret => Ansi.MoveTo(slot.Row + lines.Count - 1, slot.Column + lines[^1].Length) + colors;

    /// <summary>
    /// Word-wraps text into lines no wider than <paramref name="width"/>. Lines break after a space when
    /// they can, and the space stays on the line it ends (so the cursor lands where the next word starts);
    /// a word longer than a line is cut. Each <c>\n</c> starts a new line.
    /// </summary>
    /// <param name="value">Plain text.</param>
    /// <param name="width">The line width.</param>
    /// <returns>The lines (at least one).</returns>
    public static IReadOnlyList<string> Wrap(string value, int width)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        var result = new List<string>();
        foreach (var paragraph in value.Split('\n'))
        {
            var start = 0;
            while (paragraph.Length - start > width)
            {
                var space = paragraph.LastIndexOf(' ', start + width - 1, width);
                var end = space >= start ? space + 1 : start + width;
                result.Add(paragraph[start..end]);
                start = end;
            }

            result.Add(paragraph[start..]);
        }

        return result;
    }

    /// <summary>
    /// Replaces the text. Whatever doesn't fit (too long, too many lines, a line break in a one-line slot)
    /// is dropped from the end.
    /// </summary>
    /// <param name="value">The new text.</param>
    /// <returns>ANSI that updates the screen.</returns>
    public string Set(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var next = Multiline ? value.ReplaceLineEndings("\n") : value.ReplaceLineEndings(" ");
        while (!Fits(next))
        {
            next = next[..^1];
        }

        return Apply(next);
    }

    /// <summary>
    /// Adds a typed character, if it fits.
    /// </summary>
    /// <param name="key">One printable character.</param>
    /// <returns>ANSI that updates the screen, or an empty string when the character didn't fit.</returns>
    public string Type(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var next = text + key;
        return key.Length == 1 && !char.IsControl(key[0]) && Fits(next) ? Apply(next) : string.Empty;
    }

    /// <summary>
    /// Starts a new line (multi-line slots only), if one fits.
    /// </summary>
    /// <returns>ANSI that updates the screen, or an empty string when no line fits.</returns>
    public string NewLine()
    {
        var next = text + "\n";
        return Multiline && Fits(next) ? Apply(next) : string.Empty;
    }

    /// <summary>
    /// Removes the last character (or line break).
    /// </summary>
    /// <returns>ANSI that updates the screen, or an empty string when the box is already empty.</returns>
    public string Backspace() => text.Length == 0 ? string.Empty : Apply(text[..^1]);

    /// <summary>
    /// Removes trailing line breaks (the blank line that ended a message).
    /// </summary>
    /// <returns>ANSI that updates the screen.</returns>
    public string TrimEnd() => Apply(text.TrimEnd('\n'));

    /// <summary>
    /// Draws every line of the slot, padded to its width (for a slot that changed while the screen was hidden).
    /// </summary>
    /// <returns>Positioned ANSI for the whole slot.</returns>
    public string Render()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < slot.Height; i++)
        {
            var line = i < lines.Count ? lines[i] : string.Empty;
            sb.Append(Ansi.Reset).Append(Ansi.MoveTo(slot.Row + i, slot.Column)).Append(colors).Append(line).Append(' ', slot.Width - line.Length);
        }

        return sb.ToString();
    }

    private bool Fits(string candidate) =>
        candidate.Length <= maxLength && (Multiline
            ? Wrap(candidate, LineWidth).Count <= slot.Height
            : !candidate.Contains('\n', StringComparison.Ordinal) && candidate.Length <= LineWidth);

    private string Apply(string next)
    {
        var old = lines;
        text = next;
        lines = Multiline ? Wrap(next, LineWidth) : [next];
        var sb = new StringBuilder();
        for (var i = 0; i < Math.Max(old.Count, lines.Count); i++)
        {
            var before = i < old.Count ? old[i] : string.Empty;
            var after = i < lines.Count ? lines[i] : string.Empty;
            if (before == after)
            {
                continue;
            }

            var same = 0;
            while (same < before.Length && same < after.Length && before[same] == after[same])
            {
                same++;
            }

            sb.Append(Ansi.Reset).Append(Ansi.MoveTo(slot.Row + i, slot.Column + same)).Append(colors)
                .Append(after, same, after.Length - same).Append(' ', Math.Max(0, before.Length - after.Length));
        }

        return sb.ToString();
    }
}
