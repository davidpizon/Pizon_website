using Pizon.Doors.Sdk.Text;

namespace Pizon.Doors.Sdk.Tests;

/// <summary>
/// Tests for <see cref="TextLayout"/>: pipe-aware wrapping, boxing, fitting and padding.
/// </summary>
public sealed class TextLayoutTests
{
    /// <summary>Plain words wrap at spaces and long words are split.</summary>
    [Fact]
    public void WrapsWordsAndSplitsLongOnes()
    {
        Assert.Equal(["the quick", "brown fox"], TextLayout.Wrap("the quick brown fox", 10));
        Assert.Equal(["abcde", "fghij", "k"], TextLayout.Wrap("abcdefghijk", 5));
        Assert.Equal(["ab", "cdefg", "hi"], TextLayout.Wrap("ab cdefghi", 5));
        Assert.Equal([string.Empty], TextLayout.Wrap(string.Empty, 5));
    }

    /// <summary>Explicit line breaks are kept and tabs count as spaces.</summary>
    [Fact]
    public void KeepsLineBreaks()
    {
        Assert.Equal(["one", string.Empty, "two three"], TextLayout.Wrap("one\r\n\ntwo\tthree", 20));
    }

    /// <summary>Colors don't count toward the width and carry over to the next line.</summary>
    [Fact]
    public void CarriesColorsAcrossLines()
    {
        var lines = TextLayout.Wrap("|14|17hello there |#200|%052world", 6);
        Assert.Equal(["|14|17hello", "|14|17there", "|14|17|#200|%052world"], lines);
        Assert.All(lines, l => Assert.True(PipeCodes.VisibleLength(l) <= 6));
        Assert.Equal(["|!x y"], TextLayout.Wrap("|!x y", 5));
    }

    /// <summary>A reset or clear-screen drops the carried colors.</summary>
    [Fact]
    public void ResetStopsCarrying()
    {
        Assert.Equal(["|04a|!", "b"], TextLayout.Wrap("|04a|! b", 1));
        Assert.Equal(["|04a|CL", "b"], TextLayout.Wrap("|04a|CL b", 1));
    }

    /// <summary>A literal pipe is one visible character and never starts a code.</summary>
    [Fact]
    public void LiteralPipesAreOneCharacter()
    {
        Assert.Equal(["a||05", "b"], TextLayout.Wrap("a||05 b", 4));
        Assert.Equal(4, PipeCodes.VisibleLength("a||05"));
        Assert.Equal(["|99 |#999 |[ |{x}a|"], TextLayout.Wrap("|99 |#999 |[ |{x}a|", 40));
    }

    /// <summary>A hotspot that fits stays whole; one that is too long is closed and reopened.</summary>
    [Fact]
    public void KeepsHotspotsClickable()
    {
        Assert.Equal(["go", "|[M]main menu|]"], TextLayout.Wrap("go |[M]main menu|]", 10));
        Assert.Equal(["|[M]main|]", "|[M]menu|]"], TextLayout.Wrap("|[M]main menu|]", 5));
        Assert.Equal(["|{https://x.io}ab|]", "|{https://x.io}c|]"], TextLayout.Wrap("|{https://x.io}abc|]", 2));
        Assert.Equal(["|{ftp://x}abc|]"], TextLayout.Wrap("|{ftp://x}abc|]", 40));
    }

    /// <summary>A box keeps its height and marks dropped text with an ellipsis.</summary>
    [Fact]
    public void BoxesClipWithAnEllipsis()
    {
        Assert.Equal(["one two"], TextLayout.Box("one two", 10, 3));
        Assert.Equal(["aaaa bbbb", "cccc d..."], TextLayout.Box("aaaa bbbb cccc dddd eeee", 9, 2));
        Assert.Equal(["|[K]abc...|]"], TextLayout.Box("|[K]abcdef ghijkl|]", 6, 1).Select(l => l.Replace("|]...", "...|]", StringComparison.Ordinal)));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayout.Box("x", 5, 0));
    }

    /// <summary>Fitting cuts long text, joins lines and handles tiny widths.</summary>
    [Fact]
    public void FitsOnOneLine()
    {
        Assert.Equal("short", TextLayout.Fit("short", 10));
        Assert.Equal("a b", TextLayout.Fit("a\r\nb", 10));
        Assert.Equal("|12hello...", TextLayout.Fit("|12hello world", 8));
        Assert.Equal("ab...", TextLayout.Fit("ab cdefg", 5));
        Assert.Equal("..", TextLayout.Fit("abcdef", 2));
        Assert.Throws<ArgumentNullException>(() => TextLayout.Fit(null!, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayout.Wrap("x", 0));
    }

    /// <summary>Padding follows the alignment and never goes negative.</summary>
    [Fact]
    public void PadsForAlignment()
    {
        Assert.Equal((0, 5), TextLayout.Padding(5, 10, TextAlign.Left));
        Assert.Equal((5, 0), TextLayout.Padding(5, 10, TextAlign.Right));
        Assert.Equal((2, 3), TextLayout.Padding(5, 10, TextAlign.Center));
        Assert.Equal((0, 0), TextLayout.Padding(12, 10, TextAlign.Center));
    }
}
