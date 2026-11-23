using Pizon.Doors.Sdk.Text;

namespace Pizon.Web.Client.Screens.Rendering;

/// <summary>What a slot in the art holds.</summary>
public enum SlotKind
{
    /// <summary>Text the screen fills in (<c>@NAME@</c>).</summary>
    Field,

    /// <summary>A place the caller types into (<c>@?NAME@</c>).</summary>
    Input,

    /// <summary>A place an animation plays in (<c>@ANIM:NAME@</c>).</summary>
    Animation,
}

/// <summary>
/// A placeholder in compiled art: a box of cells the screen fills in at run time. The compiler creates one
/// static property per placeholder on the art class, so a misspelled name fails the build.
/// </summary>
/// <param name="Name">The placeholder's name as written in the art.</param>
/// <param name="Kind">What it holds.</param>
/// <param name="Row">The 1-based row of its top line.</param>
/// <param name="Column">The 1-based column of its left edge.</param>
/// <param name="Width">Its width in cells.</param>
/// <param name="Height">Its height in lines.</param>
/// <param name="Foreground">The text color for its value.</param>
/// <param name="Background">The background color for its value.</param>
/// <param name="Align">Where a short value sits.</param>
public sealed record FieldSlot(string Name, SlotKind Kind, int Row, int Column, int Width, int Height, byte Foreground, byte Background, TextAlign Align);

/// <summary>
/// A clickable label in compiled art (<c>@[K]label@</c>) that sends a key.
/// </summary>
/// <param name="Key">The key a click sends.</param>
/// <param name="Row">The 1-based row.</param>
/// <param name="Column">The 1-based column of the label's first cell.</param>
/// <param name="Width">The label's width.</param>
public sealed record HotspotSlot(string Key, int Row, int Column, int Width);

/// <summary>
/// Art compiled at build time from a <c>.dur</c> file in <c>Screens/Art</c>: its cells (with the
/// placeholders already blanked out), its slots and its hotspots. Animated art (a <c>.dur</c> with more than
/// one frame) also has its <see cref="Frames"/> and the named <see cref="FrameRanges"/> from its
/// <c>.anim.json</c>; it is played in a <c>&lt;Slot&gt;</c> of still art. The generated subclasses
/// (<c>MainMenuArt</c> and so on) live in <c>Pizon.Web.Client.Screens.Art</c>.
/// </summary>
public abstract class ScreenArt
{
    /// <summary>The name of the slot where the cursor rests after drawing.</summary>
    public const string CursorName = "CURSOR";

    private readonly string cells;
    private readonly byte[] foreground;
    private readonly byte[] background;
    private readonly Dictionary<int, string> frameAnsi = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ScreenArt"/> class.
    /// </summary>
    /// <param name="name">The art file's name without the extension.</param>
    /// <param name="columns">The width in cells.</param>
    /// <param name="rows">The height in cells.</param>
    /// <param name="cells">Every cell's character, row by row.</param>
    /// <param name="foreground">Every cell's text color, row by row.</param>
    /// <param name="background">Every cell's background color, row by row.</param>
    /// <param name="fields">The slots.</param>
    /// <param name="hotspots">The hotspots.</param>
    /// <param name="animation">The frames and ranges of animated art, or <see langword="null"/> for still art.</param>
    protected ScreenArt(string name, int columns, int rows, string cells, byte[] foreground, byte[] background, IReadOnlyList<FieldSlot> fields, IReadOnlyList<HotspotSlot> hotspots, ArtAnimation? animation = null)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(background);
        if (cells.Length != columns * rows || foreground.Length != cells.Length || background.Length != cells.Length)
        {
            throw new ArgumentException("The cell data doesn't match the art's size.", nameof(cells));
        }

        Name = name;
        Columns = columns;
        Rows = rows;
        this.cells = cells;
        this.foreground = foreground;
        this.background = background;
        Fields = fields;
        Hotspots = hotspots;
        if (animation is not null && animation.Frames.Any(f => f.Cells.Length != cells.Length))
        {
            throw new ArgumentException("Every frame must be the art's size.", nameof(animation));
        }

        Frames = animation?.Frames ?? [new ArtFrame(cells, foreground, background, 0)];
        FrameRanges = animation?.Ranges ?? new Dictionary<string, FrameRange>();
    }

    /// <summary>Gets the art file's name without the extension.</summary>
    public string Name { get; }

    /// <summary>Gets the width in cells.</summary>
    public int Columns { get; }

    /// <summary>Gets the height in cells.</summary>
    public int Rows { get; }

    /// <summary>Gets the slots, in the order they appear.</summary>
    public IReadOnlyList<FieldSlot> Fields { get; }

    /// <summary>Gets the hotspots, in the order they appear.</summary>
    public IReadOnlyList<HotspotSlot> Hotspots { get; }

    /// <summary>Gets the frames: one for still art, several for animated art.</summary>
    public IReadOnlyList<ArtFrame> Frames { get; }

    /// <summary>Gets a value indicating whether the art has more than one frame.</summary>
    public bool IsAnimated => Frames.Count > 1;

    /// <summary>Gets the named frame ranges from the art's <c>.anim.json</c>.</summary>
    public IReadOnlyDictionary<string, FrameRange> FrameRanges { get; }

    /// <summary>Gets the slot where the cursor rests, if the art has one.</summary>
    public FieldSlot? CursorSlot => Field(CursorName);

    /// <summary>Gets the art's visible text, one line per row with trailing blanks removed (for tests and the plain view).</summary>
    public string Text => string.Join('\n', Enumerable.Range(0, Rows).Select(r => cells.Substring(r * Columns, Columns).TrimEnd()));

    /// <summary>
    /// Gets a slot by name.
    /// </summary>
    /// <param name="name">The placeholder name.</param>
    /// <returns>The slot, or <see langword="null"/> when the art has none by that name.</returns>
    public FieldSlot? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);

    /// <summary>Gets a cell's character.</summary>
    /// <param name="row">The 1-based row.</param>
    /// <param name="column">The 1-based column.</param>
    /// <returns>The character.</returns>
    public char CharAt(int row, int column) => cells[Index(row, column)];

    /// <summary>Gets a cell's text color.</summary>
    /// <param name="row">The 1-based row.</param>
    /// <param name="column">The 1-based column.</param>
    /// <returns>The palette index.</returns>
    public byte ForegroundAt(int row, int column) => foreground[Index(row, column)];

    /// <summary>Gets a cell's background color.</summary>
    /// <param name="row">The 1-based row.</param>
    /// <param name="column">The 1-based column.</param>
    /// <returns>The palette index.</returns>
    public byte BackgroundAt(int row, int column) => background[Index(row, column)];

    /// <summary>
    /// Gets the frames of a named range, or of the whole art.
    /// </summary>
    /// <param name="range">A name from <see cref="FrameRanges"/> (the generated <c>Ranges</c> constants), or <see langword="null"/> for every frame.</param>
    /// <returns>The first and last frame.</returns>
    /// <exception cref="ArgumentException">The art has no range by that name.</exception>
    public FrameRange Range(string? range)
    {
        if (range is null)
        {
            return new FrameRange(0, Frames.Count - 1);
        }

        return FrameRanges.TryGetValue(range, out var found)
            ? found
            : throw new ArgumentException($"The art '{Name}' has no range '{range}' (it has: {string.Join(", ", FrameRanges.Keys.DefaultIfEmpty("none"))}).", nameof(range));
    }

    /// <summary>
    /// Draws a frame as ANSI positioned from the top left of its own box (see <see cref="ArtFrame.ToAnsi"/>).
    /// Each frame is drawn once and kept.
    /// </summary>
    /// <param name="index">The frame, counting from 0.</param>
    /// <returns>Finished ANSI.</returns>
    public string FrameAnsi(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Frames.Count);
        lock (frameAnsi)
        {
            if (!frameAnsi.TryGetValue(index, out var ansi))
            {
                ansi = Frames[index].ToAnsi(Columns);
                frameAnsi[index] = ansi;
            }

            return ansi;
        }
    }

    private int Index(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(row, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(row, Rows);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(column, Columns);
        return ((row - 1) * Columns) + column - 1;
    }
}
