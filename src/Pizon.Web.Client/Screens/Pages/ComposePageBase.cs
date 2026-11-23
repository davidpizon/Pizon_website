using Microsoft.AspNetCore.Components;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens.Forms;
using Pizon.Web.Client.Screens.Hosting;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// Writing on the message board, shared by <see cref="BoardNewPage"/> and <see cref="BoardReplyPage"/>.
/// Signed-in callers only; new posts wait for SysOp approval. The message is kept for the tab as it is
/// typed, so leaving with Escape or Back and coming back finds it again.
/// </summary>
public abstract class ComposePageBase : BbsPage
{
    /// <summary>The most lines in one message (the editor's height).</summary>
    public const int MessageLines = 7;

    /// <summary>The help line above the editor.</summary>
    public static readonly string Info = $"|07Type your message, up to {MessageLines} lines. |08A blank line sends it; ESC cancels.|07";

    /// <summary>Gets or sets the server API.</summary>
    [Inject]
    public required IBbsApi Api { get; set; }

    /// <summary>Gets the form.</summary>
    protected ComposeModel Model { get; } = new();

    /// <summary>Gets or sets the status line (pipe codes).</summary>
    protected string? Status { get; set; }

    /// <summary>Gets or sets the prompt at the cursor once the message is done.</summary>
    protected string? Prompt { get; set; }

    /// <summary>Gets or sets a value indicating whether the message has been sent or abandoned.</summary>
    protected bool Done { get; set; }

    /// <summary>Gets a value indicating whether this page answers another message.</summary>
    protected abstract bool Reply { get; }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Model.Reply = Reply;
        Model.Body = Session.ComposeDraft ?? string.Empty;
    }

    /// <summary>Keeps the message for the tab as it is typed.</summary>
    protected void SaveDraft() => Session.ComposeDraft = Model.Body;

    /// <summary>
    /// Sends the message for approval. A blank message is abandoned; a reply to a message that isn't on the
    /// wall says so and lets the caller fix the number.
    /// </summary>
    /// <returns>A task that completes when done.</returns>
    protected async Task PostAsync()
    {
        var body = Model.Body.Trim();
        if (body.Length == 0)
        {
            Session.ComposeDraft = null;
            Finish("|08Message abandoned. The wall forgives you.|07");
            return;
        }

        var token = Host.PageToken;
        if (Model.Reply)
        {
            var messages = await Api.GetMessagesAsync(token);
            if (!messages.IsOk || (messages.Value ?? []).All(m => m.Id != Model.ParentId))
            {
                Status = messages.IsOk ? "|09No such message on this wall.|07" : Ui.FailureText(messages);
                return;
            }
        }

        var result = await Api.PostMessageAsync(new PostMessageRequest(Model.Reply ? Model.ParentId : null, body), token);
        if (!result.IsOk)
        {
            Status = Ui.FailureText(result);
            return;
        }

        Session.ComposeDraft = null;
        Finish("|10MESSAGE QUEUED FOR SYSOP REVIEW.|08 It appears once approved.|07");
    }

    /// <summary>Escape leaves for the board; the message stays in the draft.</summary>
    protected void Cancel() => Host.Navigate("/board");

    /// <summary>
    /// Any key once the message is done (or for a guest, who may not write) goes back to the board.
    /// </summary>
    /// <param name="key">The key.</param>
    protected void OnKey(string key)
    {
        if (Done || !Session.IsMember)
        {
            Host.Navigate("/board");
        }
    }

    private void Finish(string status)
    {
        Status = status;
        Prompt = Ui.AnyKey;
        Done = true;
    }
}
