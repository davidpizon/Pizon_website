using System.Text;

namespace Pizon.Screens.Generator;

/// <summary>A place in the art that the screen fills in at run time.</summary>
internal sealed class ArtSlot
{
    /// <summary>Gets or sets the placeholder's name (upper case, as written in the art).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the kind: <c>Field</c>, <c>Input</c> or <c>Animation</c>.</summary>
    public string Kind { get; set; } = "Field";

    /// <summary>Gets or sets the 1-based row of the top line.</summary>
    public int Row { get; set; }

    /// <summary>Gets or sets the 1-based column of the left edge.</summary>
    public int Column { get; set; }

    /// <summary>Gets or sets the width in cells.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the height in lines.</summary>
    public int Height { get; set; } = 1;

    /// <summary>Gets or sets the text color.</summary>
    public byte Foreground { get; set; }

    /// <summary>Gets or sets the background color.</summary>
    public byte Background { get; set; }

    /// <summary>Gets or sets the alignment: <c>Left</c>, <c>Right</c> or <c>Center</c>.</summary>
    public string Align { get; set; } = "Left";
}

/// <summary>A clickable label in the art that sends a key.</summary>
internal sealed class ArtHotspot
{
    /// <summary>Gets or sets the key the click sends.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the 1-based row.</summary>
    public int Row { get; set; }

    /// <summary>Gets or sets the 1-based column of the label's first cell.</summary>
    public int Column { get; set; }

    /// <summary>Gets or sets the label's width in cells.</summary>
    public int Width { get; set; }
}

/// <summary>A problem found in the art, at a cell.</summary>
internal sealed class ArtProblem
{
    /// <summary>Gets or sets the diagnostic id (<c>PZN003</c> and up).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets the 0-based row.</summary>
    public int Row { get; set; }

    /// <summary>Gets or sets the 0-based column where the token starts.</summary>
    public int Column { get; set; }

    /// <summary>Gets or sets the token's width.</summary>
    public int Width { get; set; }
}

/// <summary>What the scanner found in one piece of art.</summary>
internal sealed class ScanResult
{
    /// <summary>Gets the slots in the order they appear.</summary>
    public List<ArtSlot> Slots { get; } = [];

    /// <summary>Gets the hotspots in the order they appear.</summary>
    public List<ArtHotspot> Hotspots { get; } = [];

    /// <summary>Gets the problems.</summary>
    public List<ArtProblem> Problems { get; } = [];
}

/// <summary>
/// Finds the placeholders in art and replaces them with blank cells. The grammar, one row at a time:
/// <list type="bullet">
/// <item><c>@NAME....@</c>: a field the whole token wide. Dots fill to the left; <c>&gt;</c> aligns right and
/// <c>=</c> centers. Its colors come from the cell after the first <c>@</c>.</item>
/// <item><c>@?NAME...@</c>: a field the visitor types into.</item>
/// <item><c>@ANIM:NAME...@</c>: a place for an animation.</item>
/// <item><c>@^......@</c>: makes the slot directly above one line taller (same column and width).</item>
/// <item><c>@[K] label@</c>: a hotspot. The label, brackets included, stays where it is drawn; only the two
/// <c>@</c> cells become blank. Clicking it sends <c>K</c>
/// (a single key, or a name such as <c>Enter</c>).</item>
/// </list>
/// Names are upper-case letters, digits and underscores, starting with a letter. An <c>@</c> that is not
/// followed by one of <c>A-Z ? ^ [</c> is an ordinary character.
/// </summary>
internal static class SlotScanner
{
    /// <summary>Property names a slot can't map to, because the art class already has them.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "Instance", "Fields", "Hotspots", "Columns", "Rows", "Name", "Text", "Field", "CharAt", "ForegroundAt",
        "BackgroundAt", "CursorSlot", "CursorName", "Equals", "GetHashCode", "GetType", "ToString", "MemberwiseClone",
        "Finalize", "ReferenceEquals",
    };

    /// <summary>
    /// Scans art for placeholders and blanks them out.
    /// </summary>
    /// <param name="grid">The art; token cells are changed to blanks.</param>
    /// <returns>The slots, hotspots and problems.</returns>
    public static ScanResult Scan(ArtGrid grid)
    {
        var result = new ScanResult();
        for (var row = 0; row < grid.Rows; row++)
        {
            var column = 0;
            while (column < grid.Columns)
            {
                column = ScanAt(grid, row, column, result);
            }
        }

        return result;
    }

    /// <summary>
    /// Turns a placeholder name into a C# property name: <c>NAME_X</c> becomes <c>NameX</c>.
    /// </summary>
    /// <param name="name">The placeholder name.</param>
    /// <returns>The property name.</returns>
    public static string PropertyName(string name)
    {
        var sb = new StringBuilder();
        foreach (var part in name.Split(['_'], StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(part[0]).Append(part.Substring(1).ToLowerInvariant());
        }

        return sb.ToString();
    }

    private static int ScanAt(ArtGrid grid, int row, int column, ScanResult result)
    {
        var chars = grid.Chars;
        var at = grid.Index(row, column);
        if (chars[at] != '@' || column + 1 >= grid.Columns || !IsTokenStart(chars[at + 1]))
        {
            return column + 1;
        }

        var end = column + 1;
        while (end < grid.Columns && chars[grid.Index(row, end)] != '@')
        {
            end++;
        }

        if (end == grid.Columns)
        {
            Problem(result, "PZN003", row, column, grid.Columns - column, "This placeholder has no closing '@' on the same row.");
            return grid.Columns;
        }

        var body = new string(chars, at + 1, end - column - 1);
        var width = end - column + 1;
        switch (body[0])
        {
            case '[':
                AddHotspot(grid, row, column, width, body, result);
                break;
            case '^':
                Continue(grid, row, column, width, body.Substring(1), result);
                break;
            case '?':
                AddSlot(grid, row, column, width, body.Substring(1), "Input", result);
                break;
            default:
                if (body.StartsWith("ANIM:", StringComparison.Ordinal))
                {
                    AddSlot(grid, row, column, width, body.Substring(5), "Animation", result);
                }
                else
                {
                    AddSlot(grid, row, column, width, body, "Field", result);
                }

                break;
        }

        return end + 1;
    }

    private static bool IsTokenStart(char c) => c is (>= 'A' and <= 'Z') or '?' or '^' or '[';

    private static void AddSlot(ArtGrid grid, int row, int column, int width, string body, string kind, ScanResult result)
    {
        var nameLength = 0;
        while (nameLength < body.Length && (body[nameLength] is >= 'A' and <= 'Z' || (nameLength > 0 && body[nameLength] is (>= '0' and <= '9') or '_')))
        {
            nameLength++;
        }

        var name = body.Substring(0, nameLength);
        var fill = body.Substring(nameLength);
        if (name.Length == 0 || !IsFill(fill))
        {
            Problem(result, "PZN003", row, column, width, "'@" + body + "@' isn't a valid placeholder: use a name in capitals followed only by '.', '>' or '='.");
            return;
        }

        if (Reserved.Contains(PropertyName(name)))
        {
            Problem(result, "PZN008", row, column, width, "'" + name + "' is reserved; pick another placeholder name.");
            return;
        }

        if (result.Slots.Any(s => s.Name == name))
        {
            Problem(result, "PZN005", row, column, width, "There is already a placeholder named '" + name + "' in this art.");
            return;
        }

        var first = grid.Index(row, column + 1);
        var slot = new ArtSlot
        {
            Name = name,
            Kind = kind,
            Row = row + 1,
            Column = column + 1,
            Width = width,
            Foreground = grid.Foreground[first],
            Background = grid.Background[first],
            Align = fill.Length == 0 || fill[0] == '.' ? "Left" : fill[0] == '>' ? "Right" : "Center",
        };
        result.Slots.Add(slot);
        Blank(grid, row, column, width, slot.Foreground, slot.Background);
    }

    private static void Continue(ArtGrid grid, int row, int column, int width, string fill, ScanResult result)
    {
        if (!IsFill(fill))
        {
            Problem(result, "PZN003", row, column, width, "A '@^' continuation may hold only '.', '>' or '='.");
            return;
        }

        var above = result.Slots.FirstOrDefault(s => s.Column == column + 1 && s.Width == width && s.Row + s.Height - 1 == row);
        if (above is null)
        {
            Problem(result, "PZN004", row, column, width, "This '@^' has no placeholder of the same column and width directly above it.");
            return;
        }

        above.Height++;
        Blank(grid, row, column, width, above.Foreground, above.Background);
    }

    private static void AddHotspot(ArtGrid grid, int row, int column, int width, string body, ScanResult result)
    {
        var close = body.IndexOf(']');
        if (close < 2)
        {
            Problem(result, "PZN003", row, column, width, "A hotspot is written '@[K] label@': the key in brackets, then the label.");
            return;
        }

        foreach (var edge in new[] { column, column + width - 1 })
        {
            grid.Chars[grid.Index(row, edge)] = ' ';
        }

        result.Hotspots.Add(new ArtHotspot { Key = body.Substring(1, close - 1), Row = row + 1, Column = column + 2, Width = body.Length });
    }

    private static bool IsFill(string fill) => fill.All(c => c is '.' or '>' or '=');

    private static void Blank(ArtGrid grid, int row, int column, int width, byte foreground, byte background)
    {
        for (var i = 0; i < width; i++)
        {
            var index = grid.Index(row, column + i);
            grid.Chars[index] = ' ';
            grid.Foreground[index] = foreground;
            grid.Background[index] = background;
        }
    }

    private static void Problem(ScanResult result, string id, int row, int column, int width, string message) =>
        result.Problems.Add(new ArtProblem { Id = id, Row = row, Column = column, Width = width, Message = message });
}
