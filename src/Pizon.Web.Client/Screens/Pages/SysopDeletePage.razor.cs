using Microsoft.AspNetCore.Components;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens.Forms;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// Delete a message (<c>/sysop/delete</c>): the SysOp types its number, then confirms with Y. The message
/// and all its replies are gone for good. Escape goes back to the console.
/// </summary>
public sealed partial class SysopDeletePage
{
    private readonly DeleteMessageModel model = new();
    private string? status;
    private string? prompt;
    private bool confirming;
    private bool done;

    /// <summary>Gets or sets the server API.</summary>
    [Inject]
    public required IBbsApi Api { get; set; }

    private void Confirm()
    {
        confirming = true;
        status = $"|09Delete #{model.Id} and all its replies? This cannot be undone. |14[Y/N]|07";
    }

    private void Keep()
    {
        confirming = false;
        status = null;
    }

    private async Task DeleteAsync()
    {
        confirming = false;
        done = true;
        var result = await Api.DeleteMessageAsync(model.Id!.Value, Host.PageToken);
        status = result.IsOk ? "|10Deleted. It never happened.|07" : Ui.FailureText(result);
        prompt = Ui.AnyKey;
    }

    private void Back() => Host.Navigate("/sysop");

    private void OnKey(string key)
    {
        if (done || !Session.IsSysop)
        {
            Back();
        }
    }
}
