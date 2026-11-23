using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Pizon.Screens.Generator.Tests;

/// <summary>
/// Tests for the screen compiler: reading Durdraw files, finding placeholders, generating C# and reporting
/// problems as build errors.
/// </summary>
public sealed class ScreenArtGeneratorTests : IDisposable
{
    /// <summary>Stand-ins for the client's runtime types, so the generated code can be compiled here.</summary>
    private const string RuntimeStubs = """
        namespace Pizon.Doors.Sdk.Text { public enum TextAlign { Left, Right, Center } }
        namespace Pizon.Web.Client.Screens.Rendering
        {
            public enum SlotKind { Field, Input, Animation }
            public sealed record FieldSlot(string Name, SlotKind Kind, int Row, int Column, int Width, int Height, byte Foreground, byte Background, Pizon.Doors.Sdk.Text.TextAlign Align);
            public sealed record HotspotSlot(string Key, int Row, int Column, int Width);
            public readonly record struct FrameRange(int First, int Last);
            public sealed class ArtFrame { public ArtFrame(string cells, byte[] foreground, byte[] background, int delayMs) { } }
            public sealed record ArtAnimation(System.Collections.Generic.IReadOnlyList<ArtFrame> Frames, System.Collections.Generic.IReadOnlyDictionary<string, FrameRange> Ranges);
            public abstract class ScreenArt
            {
                protected ScreenArt(string name, int columns, int rows, string cells, byte[] foreground, byte[] background, System.Collections.Generic.IReadOnlyList<FieldSlot> fields, System.Collections.Generic.IReadOnlyList<HotspotSlot> hotspots, ArtAnimation? animation = null) { }
            }
        }
        """;

    private readonly string folder = Directory.CreateTempSubdirectory("pizon-art-").FullName;

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>Fields, inputs, animations, blocks and hotspots become typed slots in a class that compiles.</summary>
    [Fact]
    public void CompilesPlaceholdersIntoTypedSlots()
    {
        var (sources, diagnostics) = Run(Art("main-menu.dur", [
            "Hi @HANDLE.....@ @LVL>>@ @MID===@",
            "@?INPUT..@ @[1] Go@ @[Enter] OK@",
            "@BODY.......@ \"q\" \\ é@x",
            "@^..........@",
            "@ANIM:CAM...@",
        ]));
        Assert.Empty(diagnostics);
        var source = Assert.Single(sources);
        Assert.Contains("internal sealed class MainMenuArt", source, StringComparison.Ordinal);
        Assert.Contains("Handle { get; } = new(\"HANDLE\", global::Pizon.Web.Client.Screens.Rendering.SlotKind.Field, 1, 4, 13, 1, 7, 0, global::Pizon.Doors.Sdk.Text.TextAlign.Left)", source, StringComparison.Ordinal);
        Assert.Contains("TextAlign.Right", source, StringComparison.Ordinal);
        Assert.Contains("TextAlign.Center", source, StringComparison.Ordinal);
        Assert.Contains("SlotKind.Input, 2, 1, 10", source, StringComparison.Ordinal);
        Assert.Contains("SlotKind.Field, 3, 1, 13, 2,", source, StringComparison.Ordinal);
        Assert.Contains("SlotKind.Animation, 5, 1, 13, 1", source, StringComparison.Ordinal);
        Assert.Contains("HotspotSlot(\"1\", 2, 13, 6)", source, StringComparison.Ordinal);
        Assert.Contains("HotspotSlot(\"Enter\", 2, 22, 10)", source, StringComparison.Ordinal);
        Assert.Contains("\\\"q\\\" \\\\ \\u00e9@x", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@HANDLE.", source, StringComparison.Ordinal);
        AssertCompiles(source);
    }

    /// <summary>Slot colors come from the art, and older files store colors column by column.</summary>
    /// <param name="version">The Durdraw format version.</param>
    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void ReadsColorsForEachFormatVersion(int version)
    {
        var (sources, diagnostics) = Run(Art("colors.dur", ["  @TAG@"], version: version, color: (row, column) => column >= 2 ? (11, 4) : (7, 0)));
        Assert.Empty(diagnostics);
        Assert.Contains("SlotKind.Field, 1, 3, 5, 1, 14, 4,", Assert.Single(sources), StringComparison.Ordinal);
    }

    /// <summary>Plain (not gzipped) JSON is accepted too, and files other than .dur are ignored.</summary>
    [Fact]
    public void AcceptsPlainJsonAndIgnoresOtherFiles()
    {
        var plain = Path.Combine(folder, "plain.dur");
        File.WriteAllText(plain, Json(["hello"], 8, null));
        var other = Path.Combine(folder, "notes.txt");
        File.WriteAllText(other, "@BAD");
        var (sources, diagnostics) = Run(plain, other);
        Assert.Empty(diagnostics);
        Assert.Contains("PlainArt", Assert.Single(sources), StringComparison.Ordinal);
    }

    /// <summary>Each kind of problem fails the build with its own diagnostic.</summary>
    /// <param name="id">The expected diagnostic.</param>
    /// <param name="row">An art row that causes it.</param>
    [Theory]
    [InlineData("PZN003", "@OPEN.... no close")]
    [InlineData("PZN003", "@BAD!@")]
    [InlineData("PZN003", "@?@")]
    [InlineData("PZN003", "@[]x@")]
    [InlineData("PZN004", "@^....@")]
    [InlineData("PZN008", "@INSTANCE@")]
    [InlineData("PZN008", "@CURSOR_SLOT@")]
    public void ReportsBadPlaceholders(string id, string row)
    {
        var (sources, diagnostics) = Run(Art("bad.dur", [row]));
        Assert.Empty(sources);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(0, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    /// <summary>Continuations must match the slot above, and names must be unique.</summary>
    [Fact]
    public void ReportsBadBlocksAndDuplicates()
    {
        Assert.Equal(["PZN003"], Ids(Run(Art("a.dur", ["@BODY..@", "@^xx...@"]))));
        Assert.Equal(["PZN004"], Ids(Run(Art("b.dur", ["@BODY..@", " @^....@"]))));
        Assert.Equal(["PZN005"], Ids(Run(Art("c.dur", ["@TAG@ @TAG@"]))));
    }

    /// <summary>Files the compiler can't use are reported, not silently skipped.</summary>
    [Fact]
    public void ReportsUnusableFiles()
    {
        var garbage = Path.Combine(folder, "garbage.dur");
        File.WriteAllBytes(garbage, [0x1f, 0x8b, 1, 2, 3]);
        Assert.Equal(["PZN001"], Ids(Run(garbage)));
        Assert.Equal(["PZN001"], Ids(Run(Write("notjson.dur", "{\"x\": 1}"))));
        Assert.Equal(["PZN001"], Ids(Run(Path.Combine(folder, "missing.dur"))));
        Assert.Equal(["PZN001"], Ids(Run(Write("nosize.dur", "{\"DurMovie\":{\"formatVersion\":8,\"colorFormat\":\"256\",\"columns\":0,\"lines\":1}}"))));
        Assert.Equal(["PZN001"], Ids(Run(Write("noframes.dur", "{\"DurMovie\":{\"formatVersion\":8,\"colorFormat\":\"256\",\"columns\":1,\"lines\":1,\"frames\":[]}}"))));
        Assert.Equal(["PZN001"], Ids(Run(Write("nocontents.dur", "{\"DurMovie\":{\"formatVersion\":8,\"colorFormat\":\"256\",\"columns\":1,\"lines\":1,\"frames\":[{}]}}"))));
        Assert.Equal(["PZN002"], Ids(Run(Write("sixteen.dur", "{\"DurMovie\":{\"formatVersion\":8,\"colorFormat\":\"16\"}}"))));
        Assert.Equal(["PZN006"], Ids(Run(Art("wide.dur", ["x"], columns: 81))));
        Assert.Equal(["PZN001"], Ids(Run(Write("badframe.dur", "{\"DurMovie\":{\"formatVersion\":8,\"colorFormat\":\"256\",\"columns\":1,\"lines\":1,\"frames\":[1]}}"))));
    }

    /// <summary>
    /// Art with several frames compiles to frame data with each frame's delay (its own, or one frame at the
    /// movie's rate, kept between 33 ms and 5 s), and its sidecar's ranges become typed constants.
    /// </summary>
    [Fact]
    public void CompilesAnimatedArtAndRanges()
    {
        var art = Write("sysop-cam.dur", "{\"DurMovie\":{\"formatVersion\":8,\"colorFormat\":\"256\",\"columns\":2,\"lines\":1,\"framerate\":4,\"frames\":["
            + "{\"delay\":0,\"contents\":[\"ab\"]},{\"delay\":0.001,\"contents\":[\"cd\"]},{\"delay\":60,\"contents\":[\"ef\"]},{\"delay\":0.5,\"contents\":[\"gh\"]}]}}");
        var sidecar = Write("sysop-cam.anim.json", "{ \"ranges\": { \"idle\": [0, 1], \"very-busy\": [2, 3] } }");
        var (sources, diagnostics) = Run(art, sidecar);
        Assert.Empty(diagnostics);
        var source = Assert.Single(sources);
        Assert.Contains("new(CellText, ForegroundColors, BackgroundColors, 250),", source, StringComparison.Ordinal);
        Assert.Contains("new(Frame2Text, [7,7], [0,0], 33),", source, StringComparison.Ordinal);
        Assert.Contains("new(Frame3Text, [7,7], [0,0], 5000),", source, StringComparison.Ordinal);
        Assert.Contains("new(Frame4Text, [7,7], [0,0], 500),", source, StringComparison.Ordinal);
        Assert.Contains("private const string Frame4Text =\n        \"gh\";", source.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("{ [\"idle\"] = new(0, 1), [\"very-busy\"] = new(2, 3) }", source, StringComparison.Ordinal);
        Assert.Contains("public const string VeryBusy = \"very-busy\";", source, StringComparison.Ordinal);
        AssertCompiles(source);

        var (plain, none) = Run(Art("loop.dur", ["x"], frames: 2));
        Assert.Empty(none);
        Assert.Contains("FrameRange> { })", Assert.Single(plain), StringComparison.Ordinal);
        Assert.DoesNotContain("class Ranges", plain[0], StringComparison.Ordinal);
        AssertCompiles(plain[0]);
    }

    /// <summary>Animations that can't be played, and broken sidecars, fail the build with PZN007.</summary>
    /// <param name="sidecar">The sidecar's text, or null for none.</param>
    /// <param name="frames">How many frames the art has.</param>
    /// <param name="row">The art's one row.</param>
    [Theory]
    [InlineData(null, 241, "x")]
    [InlineData(null, 2, "@TAG@")]
    [InlineData(null, 2, "@[1] go@")]
    [InlineData(null, 2, "@OPEN no close")]
    [InlineData("{}", 1, "x")]
    [InlineData("not json", 2, "x")]
    [InlineData("[]", 2, "x")]
    [InlineData("{\"loop\": true}", 2, "x")]
    [InlineData("{\"ranges\": []}", 2, "x")]
    [InlineData("{\"ranges\": {\"Idle\": [0, 1]}}", 2, "x")]
    [InlineData("{\"ranges\": {\"\": [0, 1]}}", 2, "x")]
    [InlineData("{\"ranges\": {\"idle!\": [0, 1]}}", 2, "x")]
    [InlineData("{\"ranges\": {\"idle\": [1, 0]}}", 2, "x")]
    [InlineData("{\"ranges\": {\"idle\": [0, 2]}}", 2, "x")]
    [InlineData("{\"ranges\": {\"idle\": [-1, 0]}}", 2, "x")]
    [InlineData("{\"ranges\": {\"idle\": [0.5, 1]}}", 2, "x")]
    [InlineData("{\"ranges\": {\"idle\": [0, 1.5]}}", 2, "x")]
    [InlineData("{\"ranges\": {\"idle\": [\"a\", 1]}}", 2, "x")]
    [InlineData("{\"ranges\": {\"idle\": [0, \"b\"]}}", 2, "x")]
    [InlineData("{\"ranges\": {\"idle\": [0]}}", 2, "x")]
    [InlineData("{\"ranges\": {\"idle\": 1}}", 2, "x")]
    [InlineData("{\"ranges\": {\"a-b\": [0, 0], \"a_b\": [1, 1]}}", 2, "x")]
    public void ReportsBadAnimations(string? sidecar, int frames, string row)
    {
        var paths = new List<string> { Art("anim.dur", [row], frames: frames) };
        if (sidecar is not null)
        {
            paths.Add(Write("anim.anim.json", sidecar));
        }

        var (sources, diagnostics) = Run([.. paths]);
        Assert.Empty(sources);
        Assert.Equal("PZN007", Assert.Single(diagnostics).Id);
    }

    /// <summary>A sidecar with no ranges is allowed, and one belongs only to the art file with its name.</summary>
    [Fact]
    public void MatchesSidecarsByName()
    {
        var (sources, diagnostics) = Run(Art("anim.dur", ["x"], frames: 2), Write("anim.anim.json", "{ \"ranges\": null }"), Write("other.anim.json", "{ \"ranges\": {\"x\": [0, 9]} }"));
        Assert.Empty(diagnostics);
        Assert.Single(sources);
        Assert.Null(ScreenArtGenerator.SidecarFor(Path.Combine(folder, "anim.dur"), []));
    }

    /// <summary>Durdraw's foreground numbering is mapped to the PIZON palette.</summary>
    [Fact]
    public void MapsDurdrawForegrounds()
    {
        Assert.Equal([0, 4, 2, 6, 1, 5, 3, 7, 8, 12, 10, 14, 9, 13, 11, 15, 16, 200], Enumerable.Range(0, 17).Append(200).Select(v => (int)DurReader.Foreground(v)));
    }

    /// <summary>File names become class names, and placeholder names become property names.</summary>
    [Fact]
    public void NamesClassesAndProperties()
    {
        Assert.Equal("MainMenuArt", ArtEmitter.ClassName("main-menu.dur"));
        Assert.Equal("Art404PageArt", ArtEmitter.ClassName("404 page.dur"));
        Assert.Equal("ArtArt", ArtEmitter.ClassName("---.dur"));
        Assert.Equal("ItemAccount", SlotScanner.PropertyName("ITEM_ACCOUNT"));
        Assert.Equal("\"a\\u0001\"", ArtEmitter.Literal("a\u0001"));
    }

    /// <summary>The JSON reader handles the whole JSON grammar Durdraw writes and rejects broken text.</summary>
    [Fact]
    public void ReadsJson()
    {
        var value = Assert.IsType<Dictionary<string, object?>>(MiniJson.Parse(" { \"a\": [1, -2.5e1, true, false, null, {}, []], \"s\": \"q\\\"\\\\\\/\\n\\t\\r\\b\\f\\u0041\" } "));
        Assert.Equal(new object?[] { 1d, -25d, true, false, null }, Assert.IsType<List<object?>>(value["a"]).Take(5));
        Assert.Equal("q\"\\/\n\t\r\b\fA", value["s"]);
        foreach (var bad in new[] { "", "[1,", "{\"a\" 1}", "{1:2}", "\"open", "\"\\u12\"", "tru", "-", "[1] x", "\"\\", "[1 2]" })
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse(bad));
        }
    }

    private static string[] Ids((List<string> Sources, ImmutableArray<Diagnostic> Diagnostics) result) =>
        [.. result.Diagnostics.Select(d => d.Id)];

    private static void AssertCompiles(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            "ArtCheck",
            [CSharpSyntaxTree.ParseText(RuntimeStubs), CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    private static string Json(string[] rows, int version, Func<int, int, (int Fg, int Bg)>? color, int columns = 40, int frames = 1)
    {
        var lines = Math.Max(rows.Length, 1);
        int[][][] map = version >= 8
            ? [.. Enumerable.Range(0, lines).Select(r => Enumerable.Range(0, columns).Select(c => Pair(color, r, c)).ToArray())]
            : [.. Enumerable.Range(0, columns).Select(c => Enumerable.Range(0, lines).Select(r => Pair(color, r, c)).ToArray())];
        var frame = new Dictionary<string, object> { ["frameNumber"] = 1, ["delay"] = 0, ["contents"] = rows, ["colorMap"] = map };
        var movie = new Dictionary<string, object?>
        {
            ["formatVersion"] = version,
            ["colorFormat"] = "256",
            ["name"] = "test",
            ["frames"] = Enumerable.Repeat(frame, frames).ToArray(),
            [version >= 8 ? "columns" : "sizeX"] = columns,
            [version >= 8 ? "lines" : "sizeY"] = lines,
        };
        return JsonSerializer.Serialize(new { DurMovie = movie });
    }

    private static int[] Pair(Func<int, int, (int Fg, int Bg)>? color, int row, int column)
    {
        var (fg, bg) = color?.Invoke(row, column) ?? (7, 0);
        return [fg, bg];
    }

    private static (List<string> Sources, ImmutableArray<Diagnostic> Diagnostics) Run(params string[] paths)
    {
        var compilation = CSharpCompilation.Create("Art", [CSharpSyntaxTree.ParseText(RuntimeStubs)]);
        var driver = CSharpGeneratorDriver.Create([new ScreenArtGenerator().AsSourceGenerator()], [.. paths.Select(p => (AdditionalText)new FileText(p))])
            .RunGenerators(compilation, TestContext.Current.CancellationToken);
        var result = driver.GetRunResult();
        return ([.. result.GeneratedTrees.Select(t => t.ToString())], result.Diagnostics);
    }

    private string Art(string name, string[] rows, int version = 8, Func<int, int, (int Fg, int Bg)>? color = null, int columns = 40, int frames = 1)
    {
        var path = Path.Combine(folder, name);
        using (var file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
        {
            gzip.Write(Encoding.UTF8.GetBytes(Json(rows, version, color, columns, frames)));
        }

        return path;
    }

    private string Write(string name, string json)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>An additional file the generator reads by path.</summary>
    /// <param name="path">The file's path.</param>
    private sealed class FileText(string path) : AdditionalText
    {
        /// <inheritdoc/>
        public override string Path { get; } = path;

        /// <inheritdoc/>
        public override SourceText? GetText(CancellationToken cancellationToken = default) =>
            Path.EndsWith(".json", StringComparison.Ordinal) && File.Exists(Path) ? SourceText.From(File.ReadAllText(Path)) : null;
    }
}
