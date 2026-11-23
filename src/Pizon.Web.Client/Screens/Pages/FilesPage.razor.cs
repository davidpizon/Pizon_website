using System.Text;
using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Content;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// [3] File Areas (<c>/files</c>): favorite links presented as a shareware download directory. Each area
/// opens at <c>/files/{area}</c>.
/// </summary>
public sealed partial class FilesPage
{
    private FileLibrary? library;
    private IReadOnlyList<string> items = [];

    /// <summary>Gets or sets the content store.</summary>
    [Inject]
    public required ContentStore Content { get; set; }

    /// <summary>
    /// Builds the area list lines (up to 26, lettered A to Z).
    /// </summary>
    /// <param name="library">The file library.</param>
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> Lines(FileLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return library.Areas.Take(26).Select((area, i) => Ui.ItemPipe(
            ((char)('A' + i)).ToString(),
            $"|15{TextWrap.Fit(area.Name, 12)}|07 {PipeCodes.Escape(area.Description)} |08({area.Files.Count} files)")).ToList();
    }

    /// <summary>
    /// Turns an area name into its address segment: lower case, with each run of other characters as one dash.
    /// </summary>
    /// <param name="name">The area name.</param>
    /// <returns>The segment, such as <c>dev-tools</c> for "DEV TOOLS".</returns>
    public static string Slug(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        return sb.ToString().TrimEnd('-');
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var loaded = await Content.GetFilesAsync();
        items = Lines(loaded);
        library = loaded;
    }
}
