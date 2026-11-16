using System.Globalization;
using System.Text;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// [3] File Areas: favorite links presented as a shareware download directory.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class FileAreasScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.FileAreas;

    /// <summary>
    /// Builds the area list.
    /// </summary>
    /// <param name="library">The file library.</param>
    /// <returns>ANSI text.</returns>
    public static string RenderAreas(FileLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        var sb = new StringBuilder(Ui.Cls + Ui.Header("File areas"));
        sb.Append(Ui.P("|08   All files virus-scanned with a magnifying glass. Downloads open in a new tab.|07\r\n\r\n"));
        for (var i = 0; i < library.Areas.Count && i < 26; i++)
        {
            var area = library.Areas[i];
            var letter = ((char)('A' + i)).ToString();
            sb.Append("   ").Append(Ui.Item(letter, Ui.P($"|15{TextWrap.Fit(area.Name, 12)}|07 {PipeCodes.Escape(area.Description)} |08({area.Files.Count} files)|07"))).Append("\r\n");
        }

        sb.Append("\r\n   ").Append(Ui.Item("Q", "Back to the main menu")).Append(Ui.P("\r\n\r\n|11   Select an area: |07"));
        return sb.ToString();
    }

    /// <summary>
    /// Builds one area's file listing (8.3 name, size, description).
    /// </summary>
    /// <param name="area">The area.</param>
    /// <returns>ANSI text.</returns>
    public static string RenderArea(FileArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        var sb = new StringBuilder(Ui.Cls + Ui.Header($"File area: {area.Name}"));
        sb.Append(Ui.P("|08   #  FILENAME.EXT       SIZE  DESCRIPTION|07\r\n"));
        sb.Append(Ui.P($"|08   {new string('─', 72)}|07\r\n"));
        for (var i = 0; i < area.Files.Count && i < 9; i++)
        {
            var f = area.Files[i];
            var key = (i + 1).ToString(CultureInfo.InvariantCulture);
            var name = TextWrap.Fit(f.Name.ToUpperInvariant(), 12);
            var nameText = f.Url is null ? Ui.P($"|15{name}|07") : Ansi.LinkStart(f.Url) + Ui.P($"|15{name}|07") + Ansi.LinkEnd();
            sb.Append("  ").Append(Ui.Item(key, string.Empty)).Append(' ').Append(nameText)
                .Append(Ui.P($" |11{Ui.Size(f.Size),11}|07  {PipeCodes.Escape(TextWrap.Fit(f.Description, 45))}"))
                .Append("\r\n");
        }

        sb.Append("\r\n   ").Append(Ui.Item("Q", "Back to the areas")).Append(Ui.P("\r\n\r\n|11   Download which file (1-9)? |07"));
        return sb.ToString();
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
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        var library = await Context.Content.GetFilesAsync(cancellationToken);
        while (true)
        {
            await Term.TransitionAsync(Transition.Wipe, RenderAreas(library), cancellationToken);
            var key = await Term.ReadKeyAsync(cancellationToken);
            if (DoorKeys.Is(key, 'Q') || key == DoorKeys.Escape)
            {
                return ScreenId.MainMenu;
            }

            var index = key is { Length: 1 } && char.IsAsciiLetter(key[0]) ? char.ToUpperInvariant(key[0]) - 'A' : -1;
            if (index >= 0 && index < library.Areas.Count)
            {
                await BrowseAreaAsync(library.Areas[index], cancellationToken);
            }
        }
    }

    private async Task BrowseAreaAsync(FileArea area, CancellationToken cancellationToken)
    {
        while (true)
        {
            await Term.TransitionAsync(Transition.Wipe, RenderArea(area), cancellationToken);
            var key = await Term.ReadKeyAsync(cancellationToken);
            if (DoorKeys.Is(key, 'Q') || key == DoorKeys.Escape)
            {
                return;
            }

            if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= area.Files.Count)
            {
                await DownloadAsync(area.Files[n - 1], cancellationToken);
            }
        }
    }

    private async Task DownloadAsync(FileEntry file, CancellationToken cancellationToken)
    {
        await SayAsync($"\r\n\r\n|07   Sending |15{PipeCodes.Escape(file.Name.ToUpperInvariant())}|07 ...\r\n", cancellationToken);
        foreach (var percent in new[] { 0, 17, 42, 68, 91, 100 })
        {
            await Term.WriteAsync(ProgressBar(percent), cancellationToken);
            await Term.DelayAsync(TimeSpan.FromMilliseconds(120), cancellationToken);
        }

        if (file.Url is null)
        {
            await SayAsync("\r\n|09   ERROR 404: FILE NOT FOUND ON THIS DRIVE. TRY DRIVE B:|07\r\n", cancellationToken);
        }
        else
        {
            await SayAsync($"\r\n|10   Transfer complete.|08 Opening {PipeCodes.Escape(file.Url.Host)} in a new tab...|07\r\n", cancellationToken);
            await Term.OpenUrlAsync(file.Url);
        }

        await Ui.PressAnyKeyAsync(Term, cancellationToken);
    }
}
