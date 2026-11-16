namespace Pizon.Doors.Sdk.Text;

/// <summary>
/// The 256-color ANSI palette used by PIZON BBS. Indices 0-15 use the VGA-style values from the
/// Armageddon BBS chart; 16-231 are the 6×6×6 color cube and 232-255 the grayscale ramp.
/// </summary>
public static class Palette256
{
    /// <summary>The number of colors in the palette.</summary>
    public const int Count = 256;

    private static readonly int[] SystemColors =
    [
        0x000000, 0x800000, 0x008000, 0x808000, 0x000080, 0x800080, 0x008080, 0xC0C0C0,
        0x808080, 0xFF0000, 0x00FF00, 0xFFFF00, 0x0000FF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
    ];

    private static readonly int[] CubeLevels = [0x00, 0x5F, 0x87, 0xAF, 0xD7, 0xFF];

    private static readonly int[] Table = BuildTable();

    /// <summary>
    /// Gets the 24-bit RGB value (0xRRGGBB) of a palette index.
    /// </summary>
    /// <param name="index">A palette index from 0 to 255.</param>
    /// <returns>The packed RGB value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/> is outside 0-255.</exception>
    public static int Rgb(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        return Table[index];
    }

    /// <summary>
    /// Gets the CSS hex string (for example <c>#ff00ff</c>) of a palette index.
    /// </summary>
    /// <param name="index">A palette index from 0 to 255.</param>
    /// <returns>The lowercase CSS hex color.</returns>
    public static string Hex(int index) => $"#{Rgb(index):x6}";

    /// <summary>
    /// Finds the palette index closest to an arbitrary RGB color (used to snap truecolor art).
    /// </summary>
    /// <param name="red">Red component, 0-255.</param>
    /// <param name="green">Green component, 0-255.</param>
    /// <param name="blue">Blue component, 0-255.</param>
    /// <returns>The nearest palette index by squared RGB distance.</returns>
    public static int Nearest(int red, int green, int blue)
    {
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < Count; i++)
        {
            var c = Table[i];
            var dr = ((c >> 16) & 0xFF) - red;
            var dg = ((c >> 8) & 0xFF) - green;
            var db = (c & 0xFF) - blue;
            var distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Builds the CSS custom properties <c>--c0</c> … <c>--c255</c> for the stylesheet.
    /// </summary>
    /// <returns>A CSS declaration block body, one property per line.</returns>
    public static string ToCssVariables()
    {
        var lines = new string[Count];
        for (var i = 0; i < Count; i++)
        {
            lines[i] = $"--c{i}: {Hex(i)};";
        }

        return string.Join('\n', lines);
    }

    private static int[] BuildTable()
    {
        var table = new int[Count];
        SystemColors.CopyTo(table, 0);
        var i = 16;
        foreach (var r in CubeLevels)
        {
            foreach (var g in CubeLevels)
            {
                foreach (var b in CubeLevels)
                {
                    table[i++] = (r << 16) | (g << 8) | b;
                }
            }
        }

        for (var gray = 0; gray < 24; gray++)
        {
            var level = 8 + (gray * 10);
            table[i++] = (level << 16) | (level << 8) | level;
        }

        return table;
    }
}
