using Microsoft.AspNetCore.Components;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens.Forms;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// [P] Page the SysOp (<c>/page</c>): a one-line message that buzzes the SysOp's phone. Signed-in callers
/// only. A blank message (or Escape) cancels; nothing is sent until the caller presses Enter.
/// </summary>
public sealed partial class PageSysopPage
{
    /// <summary>The longest message the input takes: the server's limit, or what fits in the slot.</summary>
    public static readonly int MaxLength = Math.Min(Limits.PageLength, Art.PageArt.Message.Width - 2);

    private readonly PageSysopModel model = new();
    private string? status;
    private string? prompt;
    private bool done;

    /// <summary>Gets or sets the server API.</summary>
    [Inject]
    public required IBbsApi Api { get; set; }

    private async Task SendAsync()
    {
        done = true;
        if (string.IsNullOrWhiteSpace(model.Message))
        {
            status = "|08Page cancelled. The SysOp sleeps on.|07";
            prompt = Ui.AnyKey;
            return;
        }

        var token = Host.PageToken;
        status = "|07Paging SysOp";
        for (var i = 0; i < 3; i++)
        {
            await Host.Terminal.BeepAsync();
            status += "|14 . ";
            await ShowAsync();
            await Host.Terminal.DelayAsync(TimeSpan.FromMilliseconds(350), token);
        }

        var result = await Api.PageSysopAsync(new PageSysopRequest(model.Message), token);
        status = result.IsOk ? "|10SysOp has been alerted.|08 If they answer, it will be in a bulletin, a reply, or a dream.|07" : Ui.FailureText(result);
        prompt = Ui.AnyKey;
    }

    private void OnKey(string key)
    {
        if (done || !Session.IsMember)
        {
            Host.Navigate("/menu");
        }
    }
}
