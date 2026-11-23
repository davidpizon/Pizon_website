using System.Globalization;
using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Screens.Components;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// One file area (<c>/files/{area}</c>): up to nine files with 8.3 names, sizes and descriptions. A number
/// "downloads" one (a ZMODEM progress bar, then the link opens in a new tab). An unknown area shows
/// "file not found".
/// </summary>
public sealed partial class FileAreaPage
{
    private bool loaded;
    private FileArea? area;
    private IReadOnlyList<string> items = [];
    private BbsScreen? screen;

    /// <summary>Gets or sets the content store.</summary>
    [Inject]
    public required ContentStore Content { get; set; }

    /// <summary>Gets or sets the area's address segment (see <see cref="FilesPage.Slug"/>).</summary>
    [Parameter]
    public string Area { get; set; } = string.Empty;

    /// <summary>
    /// Builds the file listing lines (8.3 name, size, description).
    /// </summary>
    /// <param name="area">The area.</param>
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> Lines(FileArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        return area.Files.Take(9).Select((f, i) =>
        {
            var key = (i + 1).ToString(CultureInfo.InvariantCulture);
            var name = $"|15{PipeCodes.Escape(TextWrap.Fit(f.Name.ToUpperInvariant(), 12))}|07";
            var nameText = f.Url is null ? name : $"|{{{f.Url.AbsoluteUri}}}{name}|]";
            return $"|[{key}]|14[{key}]|]|07 {nameText} |11{Ui.Size(f.Size),11}|07  {PipeCodes.Escape(TextWrap.Fit(f.Description, 45))}";
        }).ToList();
    }

    /// <summary>
    /// Builds the fake download progress bar for a percentage.
    /// </summary>
    /// <param name="percent">0-100.</param>
    /// <returns>ANSI text for one line (starting with a carriage return).</returns>
    public static string ProgressBar(int percent)
    {
        var filled = Math.Clamp(percent, 0, 100) * 30 / 100;
        return Ui.P(string.Create(CultureInfo.InvariantCulture, $"\r   |07ZMODEM |#046{new string('█', filled)}|#236{new string('░', 30 - filled)}|07 {percent,3}%"));
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var library = await Content.GetFilesAsync();
        area = library.Areas.Take(26).FirstOrDefault(a => FilesPage.Slug(a.Name).Equals(Area, StringComparison.OrdinalIgnoreCase));
        items = area is null ? [] : Lines(area);
        loaded = true;
    }

    private async Task DownloadAsync(string key)
    {
        if (area is null || !int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1 || n > Math.Min(9, area.Files.Count))
        {
            return;
        }

        var file = area.Files[n - 1];
        var term = Host.Terminal;
        var token = Host.PageToken;
        await term.WriteAsync(Ui.P($"\r\n\r\n|07   Sending |15{PipeCodes.Escape(file.Name.ToUpperInvariant())}|07 ...\r\n"), token);
        foreach (var percent in new[] { 0, 17, 42, 68, 91, 100 })
        {
            await term.WriteAsync(ProgressBar(percent), token);
            await term.DelayAsync(TimeSpan.FromMilliseconds(120), token);
        }

        if (file.Url is null)
        {
            await term.WriteAsync(Ui.P("\r\n|09   ERROR 404: FILE NOT FOUND ON THIS DRIVE. TRY DRIVE B:|07\r\n"), token);
        }
        else
        {
            await term.WriteAsync(Ui.P($"\r\n|10   Transfer complete.|08 Opening {PipeCodes.Escape(file.Url.Host)} in a new tab...|07\r\n"), token);
            await term.OpenUrlAsync(file.Url);
        }

        await Ui.PressAnyKeyAsync(term, token);
        screen?.Redraw();
    }
}
