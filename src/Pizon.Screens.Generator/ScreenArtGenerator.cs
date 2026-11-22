using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Pizon.Screens.Generator;

/// <summary>
/// The screen compiler. Every <c>.dur</c> file passed as an <c>AdditionalFiles</c> item becomes a C# art class
/// (see <see cref="ArtEmitter"/>) with a typed slot for each placeholder, so the browser only ever receives
/// compiled code and finished ANSI, never the art files or the placeholder syntax.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ScreenArtGenerator : IIncrementalGenerator
{
    /// <summary>The file could not be read as Durdraw art.</summary>
    internal static readonly DiagnosticDescriptor Unreadable = Rule("PZN001", "Art file can't be read", "{0}: {1}");

    /// <summary>The Durdraw file uses a format the compiler doesn't read.</summary>
    internal static readonly DiagnosticDescriptor Unsupported = Rule("PZN002", "Art format isn't supported", "{0}: {1}");

    /// <summary>A placeholder is malformed.</summary>
    internal static readonly DiagnosticDescriptor BadToken = Rule("PZN003", "Bad placeholder", "{0}: {1}");

    /// <summary>A continuation has nothing above it.</summary>
    internal static readonly DiagnosticDescriptor LoneContinuation = Rule("PZN004", "Continuation without a placeholder", "{0}: {1}");

    /// <summary>Two placeholders share a name.</summary>
    internal static readonly DiagnosticDescriptor Duplicate = Rule("PZN005", "Duplicate placeholder", "{0}: {1}");

    /// <summary>The art is bigger than the terminal.</summary>
    internal static readonly DiagnosticDescriptor TooLarge = Rule("PZN006", "Art is too large", "{0}: {1}");

    /// <summary>The art has more than one frame.</summary>
    internal static readonly DiagnosticDescriptor Animated = Rule("PZN007", "Animated art isn't supported yet", "{0}: {1}");

    /// <summary>A placeholder uses a name the generated class needs.</summary>
    internal static readonly DiagnosticDescriptor ReservedName = Rule("PZN008", "Reserved placeholder name", "{0}: {1}");

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var art = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".dur", StringComparison.OrdinalIgnoreCase))
            .Select((file, cancel) => Compile(file.Path, ReadBytes(file.Path)));
        context.RegisterSourceOutput(art, static (output, compiled) =>
        {
            foreach (var diagnostic in compiled.Diagnostics)
            {
                output.ReportDiagnostic(diagnostic);
            }

            if (compiled.Source is not null)
            {
                output.AddSource(compiled.HintName, SourceText.From(compiled.Source, Encoding.UTF8));
            }
        });
    }

    /// <summary>
    /// Compiles one art file.
    /// </summary>
    /// <param name="path">The file's path (for messages and the class name).</param>
    /// <param name="bytes">The file's bytes, or null when it couldn't be read.</param>
    /// <returns>The generated source (when there were no problems) and the diagnostics.</returns>
    internal static CompiledArt Compile(string path, byte[]? bytes)
    {
        var fileName = Path.GetFileName(path);
        var result = new CompiledArt(ArtEmitter.ClassName(fileName) + ".g.cs");
        if (bytes is null)
        {
            result.Diagnostics.Add(Diagnostic.Create(Unreadable, FileLocation(path), fileName, "The file can't be opened."));
            return result;
        }

        ArtGrid grid;
        try
        {
            grid = DurReader.Read(bytes);
        }
        catch (DurException ex)
        {
            var rule = ex.Problem switch
            {
                DurProblem.Unsupported => Unsupported,
                DurProblem.TooLarge => TooLarge,
                DurProblem.Animated => Animated,
                _ => Unreadable,
            };
            result.Diagnostics.Add(Diagnostic.Create(rule, FileLocation(path), fileName, ex.Message));
            return result;
        }

        var scan = SlotScanner.Scan(grid);
        foreach (var problem in scan.Problems)
        {
            var rule = problem.Id switch
            {
                "PZN004" => LoneContinuation,
                "PZN005" => Duplicate,
                "PZN008" => ReservedName,
                _ => BadToken,
            };
            var span = new LinePositionSpan(new LinePosition(problem.Row, problem.Column), new LinePosition(problem.Row, problem.Column + problem.Width));
            result.Diagnostics.Add(Diagnostic.Create(rule, Location.Create(path, default, span), fileName, problem.Message));
        }

        if (scan.Problems.Count == 0)
        {
            result.Source = ArtEmitter.Emit(fileName, grid, scan);
        }

        return result;
    }

    [SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1035:Do not use APIs banned for analyzers", Justification = "Durdraw files are gzip, which AdditionalText.GetText() would corrupt by decoding as text; the file is a declared AdditionalFiles input, so reading its bytes keeps the build deterministic.")]
    private static byte[]? ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Location FileLocation(string path) => Location.Create(path, default, default);

    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(id, title, message, "PizonScreens", DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: "https://github.com/davidpizon/Pizon_website/blob/main/docs/content.md#screen-compiler-errors");
}

/// <summary>The result of compiling one art file.</summary>
internal sealed class CompiledArt
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompiledArt"/> class.
    /// </summary>
    /// <param name="hintName">The generated file's name.</param>
    public CompiledArt(string hintName) => HintName = hintName;

    /// <summary>Gets the generated file's name.</summary>
    public string HintName { get; }

    /// <summary>Gets or sets the generated source, or null when the art had problems.</summary>
    public string? Source { get; set; }

    /// <summary>Gets the problems found.</summary>
    public List<Diagnostic> Diagnostics { get; } = [];
}
