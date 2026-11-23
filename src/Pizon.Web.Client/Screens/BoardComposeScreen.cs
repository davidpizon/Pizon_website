using System.Globalization;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// Writing on the message board (<c>/board/new</c>) or replying (<c>/board/reply</c>, which asks for the
/// message number). Signed-in callers only; new posts wait for SysOp approval. This stays a legacy screen
/// until forms arrive (phase 5 of <c>docs/plans/screen-pages.md</c>).
/// </summary>
/// <param name="context">The shared screen context.</param>
/// <param name="reply">Whether this is a reply.</param>
public sealed class BoardComposeScreen(ScreenContext context, bool reply) : ScreenBase(context)
{
    /// <summary>The width of each message line when composing.</summary>
    public const int ComposeWidth = 70;

    /// <summary>The most lines in one message.</summary>
    public const int ComposeLines = 7;

    /// <inheritdoc />
    public override ScreenId Id => reply ? ScreenId.BoardReply : ScreenId.BoardNew;

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var view = new ScreenView(BoardComposeArt.Instance).Set(BoardComposeArt.Title, reply ? "REPLY TO A MESSAGE" : "NEW MESSAGE");
        if (!session.IsMember)
        {
            view.Set(BoardComposeArt.Status, Ui.AccessDeniedText(session));
            await Term.TransitionAsync(Transition.Wipe, view.Render(), cancellationToken);
            await view.PressAnyKeyAsync(Term, cancellationToken);
            return ScreenId.MessageBoard;
        }

        long? parent = null;
        if (reply)
        {
            view.Set(BoardComposeArt.Prompt, "Reply to message #:");
            await Term.TransitionAsync(Transition.Wipe, view.Render(), cancellationToken);
            await Term.WriteAsync(view.MoveTo(BoardComposeArt.Number), cancellationToken);
            var number = await LineEditor.ReadLineAsync(Term, 9, string.Empty, cancellationToken);
            var messages = long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? (await Context.Api.GetMessagesAsync(cancellationToken)).Value ?? []
                : [];
            if (messages.All(m => m.Id != id))
            {
                return await FinishAsync(view, "|09No such message on this wall.|07", cancellationToken);
            }

            parent = id;
        }
        else
        {
            await Term.TransitionAsync(Transition.Wipe, view.Render(), cancellationToken);
        }

        await view.UpdateAsync(
            Term,
            BoardComposeArt.Info,
            $"|07Type your message, up to {ComposeLines} lines. |08A blank line sends it; ESC cancels.|07",
            cancellationToken);
        await Term.WriteAsync(view.MoveTo(BoardComposeArt.Body), cancellationToken);
        var body = await LineEditor.ReadMessageAsync(Term, ComposeLines, ComposeWidth, Limits.MessageLength, cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return await FinishAsync(view, "|08Message abandoned. The wall forgives you.|07", cancellationToken);
        }

        var result = await Context.Api.PostMessageAsync(new PostMessageRequest(parent, body), cancellationToken);
        return await FinishAsync(view, result.IsOk ? "|10MESSAGE QUEUED FOR SYSOP REVIEW.|08 It appears once approved.|07" : Ui.FailureText(result), cancellationToken);
    }

    private async Task<ScreenId> FinishAsync(ScreenView view, string status, CancellationToken cancellationToken)
    {
        await view.UpdateAsync(Term, BoardComposeArt.Status, status, cancellationToken);
        await view.PressAnyKeyAsync(Term, cancellationToken);
        return ScreenId.MessageBoard;
    }
}
