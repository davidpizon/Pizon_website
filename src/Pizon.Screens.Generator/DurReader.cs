using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Pizon.Screens.Generator;

/// <summary>
/// One frame of art as a grid of cells: a character plus a 256-color foreground and background each.
/// </summary>
internal sealed class ArtGrid
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArtGrid"/> class, filled with blank cells (light gray on black).
    /// </summary>
    /// <param name="columns">The width in cells.</param>
    /// <param name="rows">The height in cells.</param>
    public ArtGrid(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
        Chars = new char[columns * rows];
        Foreground = new byte[columns * rows];
        Background = new byte[columns * rows];
        for (var i = 0; i < Chars.Length; i++)
        {
            Chars[i] = ' ';
            Foreground[i] = 7;
        }
    }

    /// <summary>Gets the width in cells.</summary>
    public int Columns { get; }

    /// <summary>Gets the height in cells.</summary>
    public int Rows { get; }

    /// <summary>Gets the characters, row by row.</summary>
    public char[] Chars { get; }

    /// <summary>Gets the foreground colors (PIZON palette indices), row by row.</summary>
    public byte[] Foreground { get; }

    /// <summary>Gets the background colors (PIZON palette indices), row by row.</summary>
    public byte[] Background { get; }

    /// <summary>Gets the index of a cell in the arrays.</summary>
    /// <param name="row">The 0-based row.</param>
    /// <param name="column">The 0-based column.</param>
    /// <returns>The index.</returns>
    public int Index(int row, int column) => (row * Columns) + column;
}

/// <summary>
/// The frames of a piece of art and how long each one shows.
/// </summary>
internal sealed class ArtMovie
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArtMovie"/> class.
    /// </summary>
    /// <param name="frames">The frames, all the same size.</param>
    /// <param name="delays">How long each frame shows, in milliseconds.</param>
    public ArtMovie(List<ArtGrid> frames, List<int> delays)
    {
        Frames = frames;
        Delays = delays;
    }

    /// <summary>Gets the frames.</summary>
    public List<ArtGrid> Frames { get; }

    /// <summary>Gets how long each frame shows, in milliseconds.</summary>
    public List<int> Delays { get; }

    /// <summary>Gets a value indicating whether the art has more than one frame.</summary>
    public bool Animated => Frames.Count > 1;
}

/// <summary>
/// Reads Durdraw <c>.dur</c> files: gzipped (or plain) JSON with a <c>DurMovie</c> object. Handles the
/// format versions before 8 (size in <c>sizeX</c>/<c>sizeY</c>, colors stored column by column) and from 8
/// on (<c>columns</c>/<c>lines</c>, colors stored row by row). Only 256-color files are read.
/// </summary>
/// <remarks>
/// Durdraw is © Sam Foster, BSD 3-Clause; the color numbering below follows its
/// <c>color_256_to_ansi_16</c> table, where foreground values 1-16 are its own order for the 16 basic
/// colors and every other value is an xterm index.
/// </remarks>
internal static class DurReader
{
    /// <summary>The largest art the BBS terminal shows.</summary>
    public const int MaxColumns = 80;

    /// <summary>The tallest art the BBS terminal shows.</summary>
    public const int MaxRows = 25;

    /// <summary>The most frames an animation may have.</summary>
    public const int MaxFrames = 240;

    /// <summary>The shortest a frame may show, in milliseconds (about 30 frames a second).</summary>
    public const int MinDelay = 33;

    /// <summary>The longest a frame may show, in milliseconds.</summary>
    public const int MaxDelay = 5000;

    /// <summary>The frame rate Durdraw uses when a file doesn't give one.</summary>
    public const double DefaultFrameRate = 8;

    /// <summary>
    /// Reads every frame of a Durdraw file.
    /// </summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <returns>The frames, with colors in the PIZON palette, and their delays.</returns>
    /// <exception cref="DurException">The file can't be used; the message says why.</exception>
    public static ArtMovie Read(byte[] bytes)
    {
        object? root;
        try
        {
            root = MiniJson.Parse(Decode(bytes));
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            throw new DurException(DurProblem.Unreadable, ex.Message);
        }

        var movie = (root as Dictionary<string, object?>)?.TryGetValue("DurMovie", out var m) == true ? m as Dictionary<string, object?> : null;
        if (movie is null)
        {
            throw new DurException(DurProblem.Unreadable, "There is no DurMovie object.");
        }

        var version = Number(movie, "formatVersion") ?? 0;
        if (Text(movie, "colorFormat") != "256")
        {
            throw new DurException(DurProblem.Unsupported, "Only 256-color Durdraw files are supported; save it with 256 colors.");
        }

        var columns = Number(movie, version >= 8 ? "columns" : "sizeX") ?? 0;
        var rows = Number(movie, version >= 8 ? "lines" : "sizeY") ?? 0;
        if (columns < 1 || rows < 1)
        {
            throw new DurException(DurProblem.Unreadable, "The art has no size.");
        }

        if (columns > MaxColumns || rows > MaxRows)
        {
            throw new DurException(DurProblem.TooLarge, string.Format(CultureInfo.InvariantCulture, "The art is {0} x {1}; the terminal shows at most {2} x {3}.", columns, rows, MaxColumns, MaxRows));
        }

        if (movie.TryGetValue("frames", out var f) is false || f is not List<object?> frames || frames.Count == 0)
        {
            throw new DurException(DurProblem.Unreadable, "There are no frames.");
        }

        if (frames.Count > MaxFrames)
        {
            throw new DurException(DurProblem.Animated, string.Format(CultureInfo.InvariantCulture, "The animation has {0} frames; the most is {1}.", frames.Count, MaxFrames));
        }

        var rate = movie.TryGetValue("framerate", out var r) && r is double fps && fps > 0 ? fps : DefaultFrameRate;
        var result = new ArtMovie([], []);
        foreach (var item in frames)
        {
            if (item is not Dictionary<string, object?> frame)
            {
                throw new DurException(DurProblem.Unreadable, "A frame isn't an object.");
            }

            var grid = new ArtGrid(columns, rows);
            ReadContents(frame, grid);
            ReadColors(frame, grid, rowsFirst: version >= 8);
            result.Frames.Add(grid);
            result.Delays.Add(Delay(frame, rate));
        }

        return result;
    }

    /// <summary>
    /// Works out how long a frame shows: its own delay in seconds, or one frame at the movie's rate when that
    /// is 0, kept between <see cref="MinDelay"/> and <see cref="MaxDelay"/> milliseconds.
    /// </summary>
    /// <param name="frame">The frame object.</param>
    /// <param name="rate">The movie's frames per second.</param>
    /// <returns>The delay in milliseconds.</returns>
    private static int Delay(Dictionary<string, object?> frame, double rate)
    {
        var seconds = frame.TryGetValue("delay", out var d) && d is double s && s > 0 ? s : 1 / rate;
        return Math.Min(MaxDelay, Math.Max(MinDelay, (int)Math.Round(seconds * 1000)));
    }

    /// <summary>
    /// Converts a Durdraw foreground value to a PIZON palette index.
    /// </summary>
    /// <param name="value">The Durdraw value.</param>
    /// <returns>The palette index.</returns>
    public static byte Foreground(int value) => (byte)(value switch
    {
        1 => 4,
        3 => 6,
        4 => 1,
        6 => 3,
        9 => 12,
        11 => 14,
        12 => 9,
        14 => 11,
        _ => value,
    });

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static void ReadContents(Dictionary<string, object?> frame, ArtGrid grid)
    {
        if (!frame.TryGetValue("contents", out var c) || c is not List<object?> lines)
        {
            throw new DurException(DurProblem.Unreadable, "The frame has no contents.");
        }

        for (var row = 0; row < grid.Rows && row < lines.Count; row++)
        {
            var line = lines[row] as string ?? string.Empty;
            for (var column = 0; column < grid.Columns && column < line.Length; column++)
            {
                grid.Chars[grid.Index(row, column)] = char.IsControl(line[column]) ? ' ' : line[column];
            }
        }
    }

    private static void ReadColors(Dictionary<string, object?> frame, ArtGrid grid, bool rowsFirst)
    {
        if (!frame.TryGetValue("colorMap", out var c) || c is not List<object?> outer)
        {
            return;
        }

        for (var a = 0; a < outer.Count; a++)
        {
            if (outer[a] is not List<object?> inner)
            {
                continue;
            }

            for (var b = 0; b < inner.Count; b++)
            {
                var (row, column) = rowsFirst ? (a, b) : (b, a);
                if (row >= grid.Rows || column >= grid.Columns || inner[b] is not List<object?> pair || pair.Count < 2)
                {
                    continue;
                }

                var index = grid.Index(row, column);
                grid.Foreground[index] = Foreground(Color(pair[0]));
                grid.Background[index] = (byte)Color(pair[1]);
            }
        }
    }

    private static int Color(object? value) => value is double d && d >= 0 && d <= 255 ? (int)d : 0;

    private static int? Number(Dictionary<string, object?> obj, string key) =>
        obj.TryGetValue(key, out var v) && v is double d ? (int)d : null;

    private static string? Text(Dictionary<string, object?> obj, string key) =>
        obj.TryGetValue(key, out var v) ? v as string ?? (v is double d ? d.ToString(CultureInfo.InvariantCulture) : null) : null;
}

/// <summary>Why a Durdraw file couldn't be used.</summary>
internal enum DurProblem
{
    /// <summary>It isn't a Durdraw file, or it is damaged.</summary>
    Unreadable,

    /// <summary>It uses a color format the compiler doesn't read.</summary>
    Unsupported,

    /// <summary>It is bigger than the terminal.</summary>
    TooLarge,

    /// <summary>It is an animation the compiler can't use (too many frames, placeholders in it, bad ranges).</summary>
    Animated,
}

/// <summary>
/// Thrown when a Durdraw file can't be used.
/// </summary>
internal sealed class DurException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurException"/> class.
    /// </summary>
    /// <param name="problem">What is wrong.</param>
    /// <param name="message">A message for the artist.</param>
    public DurException(DurProblem problem, string message)
        : base(message) => Problem = problem;

    /// <summary>Gets what is wrong.</summary>
    public DurProblem Problem { get; }
}
