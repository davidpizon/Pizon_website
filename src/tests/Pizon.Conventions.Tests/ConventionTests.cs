using System.Globalization;
using System.Text.RegularExpressions;
using Pizon.Doors.Sdk.Text;

namespace Pizon.Conventions.Tests;

/// <summary>
/// Checks the rules from docs/AGENTS.md that the compiler can't enforce by itself.
/// </summary>
public sealed partial class ConventionTests
{
    /// <summary>Gets the <c>src</c> folder.</summary>
    private static string Src { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>Every Razor component has a code-behind whose class has an XML summary.</summary>
    [Fact]
    public void RazorComponentsHaveDocumentedCodeBehind()
    {
        var components = SourceFiles("*.razor").Where(f => !Path.GetFileName(f).StartsWith('_')).ToList();
        Assert.NotEmpty(components);
        Assert.All(components, razor =>
        {
            var codeBehind = razor + ".cs";
            Assert.True(File.Exists(codeBehind), $"{Relative(razor)} has no code-behind (.razor.cs)");
            var text = File.ReadAllText(codeBehind);
            var name = Path.GetFileNameWithoutExtension(razor);
            Assert.Matches(new Regex($@"///\s*<summary>[\s\S]*?</summary>\s*(\[[^\]]*\]\s*)*public[^\n]*partial class {name}\b"), text);
        });
    }

    /// <summary>
    /// Every screen page is a BBS page with a tab title and a declared access level (docs/plans/screen-pages.md §5, §7).
    /// </summary>
    [Fact]
    public void ScreenPagesHaveTitlesAndAccess()
    {
        var pages = Directory.EnumerateFiles(Path.Combine(Src, "Pizon.Web.Client", "Screens", "Pages"), "*.razor").ToList();
        Assert.NotEmpty(pages);
        Assert.All(pages, page =>
        {
            var text = File.ReadAllText(page);
            Assert.Matches(@"@inherits \w*(BbsPage|PageBase)\b", text);
            Assert.Contains("@attribute [BbsAccess(", text, StringComparison.Ordinal);
            Assert.Contains("<PageTitle>", text, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// No source file turns warnings off with a pragma. EF Core migrations are exempt: <c>dotnet ef</c> generates
    /// them (with its own pragma) and they are never edited by hand.
    /// </summary>
    [Fact]
    public void NoPragmaWarningDisable()
    {
        var pragma = "#pragma" + " warning disable";
        var migrations = $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}";
        var offenders = SourceFiles("*.cs").Concat(SourceFiles("*.razor"))
            .Where(f => !f.Contains(migrations, StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains(pragma, StringComparison.Ordinal))
            .Select(Relative)
            .ToList();
        Assert.Empty(offenders);
    }

    /// <summary>No project or props file hides warnings with NoWarn or WarningsNotAsErrors.</summary>
    [Fact]
    public void NoNoWarn()
    {
        var offenders = SourceFiles("*.csproj").Concat(SourceFiles("*.props")).Concat(SourceFiles("*.targets"))
            .Where(f => NoWarnRegex().IsMatch(File.ReadAllText(f)))
            .Select(Relative)
            .ToList();
        Assert.Empty(offenders);
    }

    /// <summary>The JavaScript palette (palette.js) builds the same 256 colors as Palette256.cs.</summary>
    [Fact]
    public void JavaScriptPaletteMatches()
    {
        var js = File.ReadAllText(Path.Combine(Src, "Pizon.Web.Client", "wwwroot", "js", "terminal", "palette.js"));
        var system = HexList(Section(js, "const SYSTEM"), 6);
        var cube = HexList(Section(js, "const CUBE"), 2);
        Assert.Equal(16, system.Count);
        Assert.Equal(6, cube.Count);
        Assert.Contains("8 + i * 10", js, StringComparison.Ordinal);

        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(Palette256.Rgb(i), system[i]);
        }

        var index = 16;
        foreach (var r in cube)
        {
            foreach (var g in cube)
            {
                foreach (var b in cube)
                {
                    Assert.Equal((r << 16) | (g << 8) | b, Palette256.Rgb(index++));
                }
            }
        }

        for (var i = 0; i < 24; i++)
        {
            var level = 8 + (i * 10);
            Assert.Equal((level << 16) | (level << 8) | level, Palette256.Rgb(232 + i));
        }
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Src, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Relative(string path) => Path.GetRelativePath(Src, path);

    private static string Section(string js, string start)
    {
        var from = js.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start + " not found");
        return js[from..js.IndexOf("];", from, StringComparison.Ordinal)];
    }

    private static List<int> HexList(string text, int digits) =>
        new Regex($@"0x([0-9a-fA-F]{{{digits}}})\b").Matches(text)
            .Select(m => int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToList();

    [GeneratedRegex(@"<(NoWarn|WarningsNotAsErrors)\b")]
    private static partial Regex NoWarnRegex();
}
