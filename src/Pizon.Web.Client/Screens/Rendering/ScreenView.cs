using System.Text;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens.Rendering;

/// <summary>
/// The view engine for compiled art: binds values to the art's slots and turns the result into finished
/// ANSI for the terminal, the way Razor merges a model into a compiled template. Values are pipe-code text
/// (escape visitor text with <see cref="PipeCodes.Escape"/> first); each is laid out inside its slot with
/// <see cref="TextLayout"/>, so it wraps, clips and aligns within the slot and never spills into the art.
/// </summary>
/// <param name="art">The compiled art.</param>
public sealed class ScreenView(ScreenArt art)
{
    private readonly Dictionary<FieldSlot, IReadOnlyList<string>> values = [];

    /// <summary>Gets the art.</summary>
    public ScreenArt Art { get; } = art ?? throw new ArgumentNullException(nameof(art));

    /// <summary>
    /// Binds text to a slot. A one-line slot fits it on the line (with "..." when cut); a taller slot
    /// word-wraps it and keeps as many lines as fit.
    /// </summary>
    /// <param name="slot">A slot of this art.</param>
    /// <param name="pipeText">The value.</param>
    /// <returns>This view, for chaining.</returns>
    public ScreenView Set(FieldSlot slot, string pipeText)
    {
        Check(slot);
        ArgumentNullException.ThrowIfNull(pipeText);
        values[slot] = slot.Height == 1 ? [TextLayout.Fit(pipeText, slot.Width)] : TextLayout.Box(pipeText, slot.Width, slot.Height);
        return this;
    }

    /// <summary>
    /// Binds one value per line of a slot (a list). Each line is fitted to the width; lines past the
    /// slot's height are dropped.
    /// </summary>
    /// <param name="slot">A slot of this art.</param>
    /// <param name="pipeLines">The lines.</param>
    /// <returns>This view, for chaining.</returns>
    public ScreenView SetLines(FieldSlot slot, IEnumerable<string> pipeLines)
    {
        Check(slot);
        ArgumentNullException.ThrowIfNull(pipeLines);
        values[slot] = pipeLines.Take(slot.Height).Select(l => TextLayout.Fit(l, slot.Width)).ToList();
        return this;
    }

    /// <summary>
    /// Gets the lines bound to a slot.
    /// </summary>
    /// <param name="slot">A slot of this art.</param>
    /// <returns>The laid-out lines, or an empty list when nothing is bound.</returns>
    public IReadOnlyList<string> Get(FieldSlot slot)
    {
        Check(slot);
        return values.TryGetValue(slot, out var lines) ? lines : [];
    }

    /// <summary>
    /// Draws the whole screen: clears it, paints every cell, writes the bound values and leaves the cursor
    /// at the <c>@CURSOR@</c> slot.
    /// </summary>
    /// <returns>Finished ANSI.</returns>
    public string Render()
    {
        var sb = new StringBuilder(Ui.Cls);
        for (var row = 1; row <= Art.Rows; row++)
        {
            PaintRow(sb, row);
        }

        foreach (var slot in Art.Fields.Where(values.ContainsKey))
        {
            PaintField(sb, slot);
        }

        return sb.Append(Ansi.Reset).Append(CursorHome()).ToString();
    }

    /// <summary>
    /// Redraws one slot (for a live update) and puts the cursor back at the <c>@CURSOR@</c> slot.
    /// </summary>
    /// <param name="slot">A slot of this art.</param>
    /// <returns>Positioned ANSI for just that slot.</returns>
    public string RenderField(FieldSlot slot)
    {
        Check(slot);
        var sb = new StringBuilder();
        PaintField(sb, slot);
        return sb.Append(Ansi.Reset).Append(CursorHome()).ToString();
    }

    /// <summary>
    /// Moves the cursor to the <c>@CURSOR@</c> slot in that slot's colors.
    /// </summary>
    /// <returns>ANSI, or an empty string when the art has no cursor slot.</returns>
    public string CursorHome() => Art.CursorSlot is { } c ? Ansi.MoveTo(c.Row, c.Column) + Ansi.Fg(c.Foreground) + Ansi.Bg(c.Background) : string.Empty;

    /// <summary>
    /// Moves the cursor to the start of a slot in its colors (for typing into an <c>@?NAME@</c> slot).
    /// </summary>
    /// <param name="slot">A slot of this art.</param>
    /// <param name="line">The 0-based line within the slot.</param>
    /// <returns>ANSI.</returns>
    public string MoveTo(FieldSlot slot, int line = 0)
    {
        Check(slot);
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, slot.Height);
        return Ansi.MoveTo(slot.Row + line, slot.Column) + Ansi.Fg(slot.Foreground) + Ansi.Bg(slot.Background);
    }

    /// <summary>
    /// Shows lines in a slot a page at a time. While more pages follow, <paramref name="more"/> shows a
    /// prompt; Enter or any other key turns the page and Q or Escape stops.
    /// </summary>
    /// <param name="terminal">The terminal.</param>
    /// <param name="slot">The slot the lines go in.</param>
    /// <param name="more">The slot for the "more" prompt.</param>
    /// <param name="lines">The lines.</param>
    /// <param name="cancellationToken">Cancels paging.</param>
    /// <returns><see langword="true"/> when the last page was reached; <see langword="false"/> when the caller stopped early.</returns>
    public async Task<bool> PageAsync(ITerminal terminal, FieldSlot slot, FieldSlot more, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(lines);
        Check(more);
        for (var start = 0; ; start += slot.Height)
        {
            SetLines(slot, lines.Skip(start));
            var last = start + slot.Height >= lines.Count;
            Set(more, last ? string.Empty : "|08-- more -- |[Enter]|14[Enter]|08 next page|]  |[Q]|14[Q]|08 stop|]");
            await terminal.WriteAsync(RenderField(slot) + RenderField(more), cancellationToken);
            if (last)
            {
                return true;
            }

            var key = await terminal.ReadKeyAsync(cancellationToken);
            if (DoorKeys.Is(key, 'Q') || key == DoorKeys.Escape)
            {
                Set(more, string.Empty);
                await terminal.WriteAsync(RenderField(more), cancellationToken);
                return false;
            }
        }
    }

    /// <summary>
    /// Binds a value to a slot and redraws just that slot.
    /// </summary>
    /// <param name="terminal">The terminal.</param>
    /// <param name="slot">A slot of this art.</param>
    /// <param name="pipeText">The value.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when written.</returns>
    public Task UpdateAsync(ITerminal terminal, FieldSlot slot, string pipeText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        Set(slot, pipeText);
        return terminal.WriteAsync(RenderField(slot), cancellationToken);
    }

    /// <summary>
    /// Shows the "press any key" prompt at the <c>@CURSOR@</c> slot and waits.
    /// </summary>
    /// <param name="terminal">The terminal.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The key pressed.</returns>
    public async Task<string> PressAnyKeyAsync(ITerminal terminal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        await terminal.WriteAsync(CursorHome() + PipeCodes.ToAnsi(Ui.AnyKey), cancellationToken);
        return await terminal.ReadKeyAsync(cancellationToken);
    }

    private void PaintRow(StringBuilder sb, int row)
    {
        var last = Art.Columns;
        while (last > 0 && Art.CharAt(row, last) == ' ' && Art.BackgroundAt(row, last) == 0)
        {
            last--;
        }

        var hotspots = Art.Hotspots.Where(h => h.Row == row).ToList();
        last = hotspots.Select(h => h.Column + h.Width - 1).Append(last).Max();
        if (last == 0)
        {
            return;
        }

        sb.Append(Ansi.MoveTo(row, 1));
        int fg = -1, bg = -1;
        for (var column = 1; column <= last; column++)
        {
            if (hotspots.FirstOrDefault(h => h.Column == column) is { } start)
            {
                sb.Append(Ansi.HotspotStart(start.Key));
            }

            if (Art.ForegroundAt(row, column) != fg)
            {
                fg = Art.ForegroundAt(row, column);
                sb.Append(Ansi.Fg(fg));
            }

            if (Art.BackgroundAt(row, column) != bg)
            {
                bg = Art.BackgroundAt(row, column);
                sb.Append(Ansi.Bg(bg));
            }

            sb.Append(Art.CharAt(row, column));
            if (hotspots.Any(h => h.Column + h.Width - 1 == column))
            {
                sb.Append(Ansi.LinkEnd());
            }
        }
    }

    private void PaintField(StringBuilder sb, FieldSlot slot)
    {
        var lines = values.TryGetValue(slot, out var bound) ? bound : [];
        var colors = Ansi.Fg(slot.Foreground) + Ansi.Bg(slot.Background);
        for (var i = 0; i < slot.Height; i++)
        {
            var line = i < lines.Count ? lines[i] : string.Empty;
            var (before, after) = TextLayout.Padding(PipeCodes.VisibleLength(line), slot.Width, slot.Align);
            sb.Append(Ansi.Reset).Append(Ansi.MoveTo(slot.Row + i, slot.Column)).Append(colors).Append(' ', before)
                .Append(PipeCodes.ToAnsi(line)).Append(Ansi.Reset).Append(colors).Append(' ', after);
        }
    }

    private void Check(FieldSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (!Art.Fields.Contains(slot))
        {
            throw new ArgumentException($"'{slot.Name}' is not a slot of the {Art.Name} art.", nameof(slot));
        }
    }
}
