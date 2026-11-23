using System.Globalization;
using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// The pages inbox (<c>/sysop/pages</c>): every page callers sent, unread ones starred. The inbox is
/// fetched once the screen is on; once the SysOp has seen its last page, leaving (any key) marks the
/// unread ones read. A link alone never marks anything.
/// </summary>
public sealed partial class SysopPagesPage
{
    private static readonly IReadOnlyList<string> NoLines = [];
    private IReadOnlyList<SysopPageDto> pages = [];
    private IReadOnlyList<string>? lines;
    private string? prompt;
    private bool seen;

    /// <summary>Gets or sets the server API.</summary>
    [Inject]
    public required IBbsApi Api { get; set; }

    /// <summary>
    /// Builds the inbox lines: two per page (who and when, then the message).
    /// </summary>
    /// <param name="result">The inbox.</param>
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> Lines(ApiResult<IReadOnlyList<SysopPageDto>> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var items = result.Value ?? [];
        var lines = items.SelectMany(p => new[]
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"  {(p.Read ? "|08 " : "|10*")} |14#{p.Id}|08 · |15{PipeCodes.Escape(p.Handle)}|08 · {Ui.Stamp(p.SentAt)}|07"),
            $"      |07{PipeCodes.Escape(p.Message)}|07",
        }).ToList();
        if (!result.IsOk)
        {
            lines.Add(Ui.FailureText(result));
        }
        else if (items.Count == 0)
        {
            lines.Add("|08  No pages. Nobody needs you. Enjoy it.|07");
        }

        return lines;
    }

    private async Task LoadAsync()
    {
        if (lines is not null || !Session.IsSysop)
        {
            return;
        }

        var result = await Api.GetPagesAsync(Host.PageToken);
        pages = result.Value ?? [];
        lines = Lines(result);
    }

    private void LastPage()
    {
        if (lines is null)
        {
            return;
        }

        seen = true;
        prompt = Ui.AnyKey;
    }

    private async Task LeaveAsync()
    {
        if (seen)
        {
            foreach (var page in pages.Where(p => !p.Read))
            {
                _ = await Api.MarkPageReadAsync(page.Id, Host.PageToken);
            }
        }

        Host.Navigate("/sysop");
    }
}
