using Pizon.Doors.Sdk.Text;

namespace Pizon.Doors.Sdk.Tests;

/// <summary>
/// Tests for the 256-color palette.
/// </summary>
public sealed class Palette256Tests
{
    /// <summary>Indices 0-15 match the Armageddon BBS chart's VGA-style values.</summary>
    /// <param name="index">The palette index.</param>
    /// <param name="hex">The expected color.</param>
    [Theory]
    [InlineData(0, "#000000")]
    [InlineData(1, "#800000")]
    [InlineData(7, "#c0c0c0")]
    [InlineData(8, "#808080")]
    [InlineData(11, "#ffff00")]
    [InlineData(14, "#00ffff")]
    [InlineData(15, "#ffffff")]
    public void StandardColorsMatchTheChart(int index, string hex) => Assert.Equal(hex, Palette256.Hex(index));

    /// <summary>The 6x6x6 cube and the gray ramp use the xterm levels.</summary>
    [Fact]
    public void CubeAndGraysUseXtermLevels()
    {
        Assert.Equal("#000000", Palette256.Hex(16));
        Assert.Equal("#5f87af", Palette256.Hex(16 + 36 + 12 + 3));
        Assert.Equal("#ffffff", Palette256.Hex(231));
        Assert.Equal("#080808", Palette256.Hex(232));
        Assert.Equal("#eeeeee", Palette256.Hex(255));
    }

    /// <summary>Out-of-range indices are rejected.</summary>
    [Fact]
    public void RejectsOutOfRangeIndices()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Palette256.Rgb(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Palette256.Rgb(256));
    }

    /// <summary>Nearest finds exact matches and snaps near colors.</summary>
    [Fact]
    public void NearestSnapsToThePalette()
    {
        Assert.Equal(9, Palette256.Nearest(255, 0, 0));
        Assert.Equal(0, Palette256.Nearest(1, 1, 1));
        Assert.Equal(0xFF0000, Palette256.Rgb(Palette256.Nearest(250, 5, 5)));
    }

    /// <summary>The CSS variables list every color.</summary>
    [Fact]
    public void CssVariablesListEveryColor()
    {
        var css = Palette256.ToCssVariables();
        Assert.Contains("--c0: #000000;", css, StringComparison.Ordinal);
        Assert.Contains("--c255: #eeeeee;", css, StringComparison.Ordinal);
    }
}

/// <summary>
/// Tests for the ANSI builders.
/// </summary>
public sealed class AnsiTests
{
    /// <summary>Cursor and color sequences are well formed.</summary>
    [Fact]
    public void BuildsSequences()
    {
        Assert.Equal("\u001b[5;10H", Ansi.MoveTo(5, 10));
        Assert.Equal("\u001b[38;5;201m", Ansi.Fg(201));
        Assert.Equal("\u001b[48;5;4m", Ansi.Bg(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ansi.Fg(256));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ansi.Bg(-1));
    }

    /// <summary>Hotspots and links are OSC 8 sequences.</summary>
    [Fact]
    public void BuildsHotspotsAndLinks()
    {
        Assert.Equal("\u001b]8;;key:Q\u001b\\Quit\u001b]8;;\u001b\\", Ansi.Hotspot("Q", "Quit"));
        Assert.Equal("\u001b]8;;https://example.com/\u001b\\", Ansi.LinkStart(new Uri("https://example.com")));
        Assert.Throws<ArgumentException>(() => Ansi.HotspotStart(string.Empty));
        Assert.Throws<ArgumentNullException>(() => Ansi.LinkStart(null!));
    }
}

/// <summary>
/// Tests for pipe-code translation and escaping.
/// </summary>
public sealed class PipeCodesTests
{
    /// <summary>Classic codes set foreground (00-15) and background (16-23).</summary>
    [Fact]
    public void TranslatesClassicCodes()
    {
        Assert.Equal(Ansi.Fg(14) + "hi" + Ansi.Bg(1), PipeCodes.ToAnsi("|14hi|17"));
        Assert.Equal("|24", PipeCodes.ToAnsi("|24"));
        Assert.Equal("|ab", PipeCodes.ToAnsi("|ab"));
        Assert.Equal("end|", PipeCodes.ToAnsi("end|"));
    }

    /// <summary>Extended codes reach all 256 colors and reject bad indices.</summary>
    [Fact]
    public void TranslatesExtendedCodes()
    {
        Assert.Equal(Ansi.Fg(201) + Ansi.Bg(17), PipeCodes.ToAnsi("|#201|%017"));
        Assert.Equal("|#999", PipeCodes.ToAnsi("|#999"));
        Assert.Equal("|#12", PipeCodes.ToAnsi("|#12"));
    }

    /// <summary>Specials: literal pipe, reset, clear screen, hotspots and links.</summary>
    [Fact]
    public void TranslatesSpecials()
    {
        Assert.Equal("a|b", PipeCodes.ToAnsi("a||b"));
        Assert.Equal(Ansi.Reset, PipeCodes.ToAnsi("|!"));
        Assert.Equal(Ansi.ClearScreen, PipeCodes.ToAnsi("|CL"));
        Assert.Equal(Ansi.Hotspot("P", "Page"), PipeCodes.ToAnsi("|[P]Page|]"));
        Assert.Equal(Ansi.LinkStart(new Uri("https://pizon.com/")) + "site" + Ansi.LinkEnd(), PipeCodes.ToAnsi("|{https://pizon.com/}site|]"));
    }

    /// <summary>Only http and https links are allowed; others stay as text.</summary>
    [Fact]
    public void RejectsUnsafeLinks()
    {
        Assert.Equal("|{javascript:alert(1)}x", PipeCodes.ToAnsi("|{javascript:alert(1)}x"));
        Assert.Equal("|[]x", PipeCodes.ToAnsi("|[]x"));
        Assert.Equal("|[open", PipeCodes.ToAnsi("|[open"));
    }

    /// <summary>Escaping visitor text neutralizes pipes and control characters.</summary>
    [Fact]
    public void EscapeNeutralizesVisitorText()
    {
        var escaped = PipeCodes.Escape("|14red\u001b[2J|[X]");
        Assert.Equal("||14red[2J||[X]", escaped);
        Assert.Equal("|14red[2J|[X]", PipeCodes.ToAnsi(escaped));
    }

    /// <summary>Plain text and visible length ignore codes.</summary>
    [Fact]
    public void MeasuresVisibleText()
    {
        Assert.Equal("Hi there", PipeCodes.StripToPlain("|14Hi |#201there|!"));
        Assert.Equal(4, PipeCodes.VisibleLength("|[Q]Quit|]"));
        Assert.Equal("ab", PipeCodes.StripAnsi("a\u001b7b\u001b]8;;x"));
        Assert.Equal("x\u001b", PipeCodes.StripAnsi("x\u001b"));
    }
}

/// <summary>
/// Tests for wrapping and fitting text.
/// </summary>
public sealed class TextWrapTests
{
    /// <summary>Words wrap at the width; long words are split; paragraphs are kept.</summary>
    [Fact]
    public void WrapsWords()
    {
        Assert.Equal(["the quick", "brown fox"], TextWrap.Wrap("the quick brown fox", 10));
        Assert.Equal(["ab", "abcdef", "ghij"], TextWrap.Wrap("ab abcdefghij", 6));
        Assert.Equal(["one", string.Empty, "two"], TextWrap.Wrap("one\r\n\r\ntwo", 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextWrap.Wrap("x", 0));
    }

    /// <summary>Fit pads or truncates; Center centers or truncates.</summary>
    [Fact]
    public void FitsAndCenters()
    {
        Assert.Equal("ab  ", TextWrap.Fit("ab", 4));
        Assert.Equal("abc", TextWrap.Fit("abcdef", 3));
        Assert.Equal(" ab  ", TextWrap.Center("ab", 5));
        Assert.Equal("abc", TextWrap.Center("abcdef", 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextWrap.Center("x", -1));
    }
}
