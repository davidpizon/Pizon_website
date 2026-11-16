using System.Text;
using Pizon.Doors.Sdk.Text;

namespace Pizon.Doors.Oracle;

/// <summary>
/// Draws a fortune as an ANSI card with one of several border styles and a 256-color gradient.
/// </summary>
public static class FortuneCard
{
    /// <summary>The inner text width of a card.</summary>
    public const int InnerWidth = 44;

    /// <summary>The left margin, so the 48-column card sits centered on an 80-column screen.</summary>
    public const int Margin = 16;

    private static readonly string[][] Borders =
    [
        ["┌", "─", "┐", "│", "└", "┘"],
        ["╔", "═", "╗", "║", "╚", "╝"],
        ["▛", "▀", "▜", "▌", "▙", "▟"],
        ["░", "░", "░", "░", "░", "░"],
    ];

    private static readonly int[][] Gradients =
    [
        [196, 202, 208, 214, 220, 226],
        [51, 45, 39, 33, 27, 21],
        [201, 165, 129, 93, 57, 21],
        [46, 47, 48, 49, 50, 51],
    ];

    /// <summary>Gets the number of border styles available.</summary>
    public static int StyleCount => Borders.Length;

    /// <summary>
    /// Renders a fortune card.
    /// </summary>
    /// <param name="fortune">The fortune text (plain).</param>
    /// <param name="seed">Chooses the border and gradient, so a question always gets the same card.</param>
    /// <returns>The card as ANSI text, one line per row, each ending in CR LF.</returns>
    public static string Render(string fortune, uint seed)
    {
        ArgumentNullException.ThrowIfNull(fortune);
        var border = Borders[(int)(seed % (uint)Borders.Length)];
        var gradient = Gradients[(int)((seed / 7) % (uint)Gradients.Length)];
        var body = TextWrap.Wrap(fortune, InnerWidth - 2);
        var rows = new List<string> { string.Empty, "THE ORACLE SPEAKS", string.Empty };
        rows.AddRange(body);
        rows.Add(string.Empty);

        var pad = new string(' ', Margin);
        var sb = new StringBuilder();
        sb.Append(pad).Append(Ansi.Fg(gradient[0]))
            .Append(border[0]).Append(Repeat(border[1], InnerWidth)).Append(border[2]).Append(Ansi.NewLine);
        for (var i = 0; i < rows.Count; i++)
        {
            var color = gradient[Math.Min(gradient.Length - 1, (i * gradient.Length) / rows.Count)];
            var text = i == 1 ? TextWrap.Center(rows[i], InnerWidth) : " " + TextWrap.Fit(rows[i], InnerWidth - 1);
            var textColor = i == 1 ? 15 : 7;
            sb.Append(pad).Append(Ansi.Fg(color)).Append(border[3])
                .Append(Ansi.Fg(textColor)).Append(text)
                .Append(Ansi.Fg(color)).Append(border[3]).Append(Ansi.NewLine);
        }

        sb.Append(pad).Append(Ansi.Fg(gradient[^1]))
            .Append(border[4]).Append(Repeat(border[1], InnerWidth)).Append(border[5]).Append(Ansi.Reset).Append(Ansi.NewLine);
        return sb.ToString();
    }

    private static string Repeat(string s, int count) => string.Concat(Enumerable.Repeat(s, count));
}
