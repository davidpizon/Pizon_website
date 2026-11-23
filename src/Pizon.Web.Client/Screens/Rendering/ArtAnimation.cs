using System.Text;
using Pizon.Doors.Sdk.Text;

namespace Pizon.Web.Client.Screens.Rendering;

/// <summary>
/// A run of frames in animated art, first and last counting from 0, named in the art's
/// <c>.anim.json</c> so a page can pick one (<c>&lt;Slot Range="..."&gt;</c>).
/// </summary>
/// <param name="First">The first frame.</param>
/// <param name="Last">The last frame.</param>
public readonly record struct FrameRange(int First, int Last);

/// <summary>
/// One frame of compiled art: every cell's character and colors, and how long the frame shows.
/// </summary>
public sealed class ArtFrame
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArtFrame"/> class.
    /// </summary>
    /// <param name="cells">Every cell's character, row by row.</param>
    /// <param name="foreground">Every cell's text color, row by row.</param>
    /// <param name="background">Every cell's background color, row by row.</param>
    /// <param name="delayMs">How long the frame shows, in milliseconds.</param>
    public ArtFrame(string cells, byte[] foreground, byte[] background, int delayMs)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(background);
        if (foreground.Length != cells.Length || background.Length != cells.Length)
        {
            throw new ArgumentException("The frame's colors don't match its cells.", nameof(foreground));
        }

        Cells = cells;
        Foreground = foreground;
        Background = background;
        DelayMs = delayMs;
    }

    /// <summary>Gets every cell's character, row by row.</summary>
    public string Cells { get; }

    /// <summary>Gets every cell's text color, row by row.</summary>
    internal byte[] Foreground { get; }

    /// <summary>Gets every cell's background color, row by row.</summary>
    internal byte[] Background { get; }

    /// <summary>Gets how long the frame shows, in milliseconds.</summary>
    public int DelayMs { get; }

    /// <summary>
    /// Draws the frame as ANSI positioned from the top left of its own box (row 1, column 1), the way the
    /// terminal draws a slot: each row from its first column, trailing blanks left out.
    /// </summary>
    /// <param name="columns">The frame's width.</param>
    /// <returns>Finished ANSI.</returns>
    public string ToAnsi(int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        var sb = new StringBuilder();
        for (var row = 0; row * columns < Cells.Length; row++)
        {
            var start = row * columns;
            var last = columns;
            while (last > 0 && Cells[start + last - 1] == ' ' && Background[start + last - 1] == 0)
            {
                last--;
            }

            if (last == 0)
            {
                continue;
            }

            sb.Append(Ansi.MoveTo(row + 1, 1));
            int fg = -1, bg = -1;
            for (var i = start; i < start + last; i++)
            {
                if (Foreground[i] != fg)
                {
                    fg = Foreground[i];
                    sb.Append(Ansi.Fg(fg));
                }

                if (Background[i] != bg)
                {
                    bg = Background[i];
                    sb.Append(Ansi.Bg(bg));
                }

                sb.Append(Cells[i]);
            }
        }

        return sb.Append(Ansi.Reset).ToString();
    }
}

/// <summary>
/// The frames of animated art and its named ranges, passed by the generated art class to
/// <see cref="ScreenArt"/>.
/// </summary>
/// <param name="Frames">Every frame; the first is the art's own cells.</param>
/// <param name="Ranges">The named ranges from the art's <c>.anim.json</c> (often none).</param>
public sealed record ArtAnimation(IReadOnlyList<ArtFrame> Frames, IReadOnlyDictionary<string, FrameRange> Ranges);
