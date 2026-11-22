using System.Globalization;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// [2] Message Boards: a threaded, text-only guestbook. Anyone can read; signed-in callers can post and
/// reply. New posts wait for SysOp approval.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class MessageBoardScreen(ScreenContext context) : ScreenBase(context)
{
    /// <summary>The width of each message line when composing.</summary>
    public const int ComposeWidth = 70;

    /// <summary>The most lines in one message.</summary>
    public const int ComposeLines = 7;

    /// <inheritdoc />
    public override ScreenId Id => ScreenId.MessageBoard;

    /// <summary>
    /// Orders messages as threads: each top-level message followed by its replies (depth-first, oldest first).
    /// </summary>
    /// <param name="messages">Approved messages.</param>
    /// <returns>(message, depth) pairs in display order.</returns>
    public static IReadOnlyList<(MessageDto Message, int Depth)> Thread(IReadOnlyList<MessageDto> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var ids = messages.Select(m => m.Id).ToHashSet();
        var children = messages
            .Where(m => m.ParentId is { } p && ids.Contains(p))
            .GroupBy(m => m.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.PostedAt).ToList());
        var result = new List<(MessageDto, int)>();
        void Visit(MessageDto m, int depth)
        {
            result.Add((m, depth));
            if (children.TryGetValue(m.Id, out var kids))
            {
                foreach (var k in kids)
                {
                    Visit(k, Math.Min(depth + 1, 4));
                }
            }
        }

        foreach (var root in messages.Where(m => m.ParentId is null || !ids.Contains(m.ParentId.Value)).OrderBy(m => m.PostedAt))
        {
            Visit(root, 0);
        }

        return result;
    }

    /// <summary>
    /// Formats one message for the listing.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="depth">Its reply depth.</param>
    /// <returns>Pipe-code lines that fit the board's message block.</returns>
    public static IReadOnlyList<string> FormatMessage(MessageDto message, int depth)
    {
        ArgumentNullException.ThrowIfNull(message);
        var indent = new string(' ', 2 + (depth * 3));
        var marker = depth == 0 ? "■" : "└";
        var lines = new List<string>
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"{indent}|#039{marker} |14#{message.Id}|08 · |15{PipeCodes.Escape(message.Handle)}|08 · {Ui.Stamp(message.PostedAt)}|07"),
        };
        var width = Math.Max(20, BoardArt.Body.Width - indent.Length - 2);
        lines.AddRange(TextLayout.Wrap(PipeCodes.Escape(message.Body), width).Select(l => $"{indent}  |07{l}"));
        return lines;
    }

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        while (true)
        {
            var view = new ScreenView(BoardArt.Instance);
            await Term.TransitionAsync(Transition.Wipe, view.Render(), cancellationToken);
            var result = await Context.Api.GetMessagesAsync(cancellationToken);
            var messages = result.Value ?? [];
            IReadOnlyList<string> lines = !result.IsOk
                ? [Ui.FailureText(result)]
                : messages.Count == 0
                    ? ["|08  The wall is blank. Somebody has to go first.|07"]
                    : Thread(messages).SelectMany(t => FormatMessage(t.Message, t.Depth).Append(string.Empty)).ToList();
            await view.PageAsync(Term, BoardArt.Body, BoardArt.More, lines, cancellationToken);
            var key = await Term.ReadKeyAsync(cancellationToken);
            if (DoorKeys.Is(key, 'Q') || key == DoorKeys.Escape)
            {
                return ScreenId.MainMenu;
            }

            if (DoorKeys.Is(key, 'N') || DoorKeys.Is(key, 'R'))
            {
                await ComposeAsync(session, DoorKeys.Is(key, 'R'), messages, cancellationToken);
            }
        }
    }

    private async Task ComposeAsync(BbsSession session, bool reply, IReadOnlyList<MessageDto> messages, CancellationToken cancellationToken)
    {
        var view = new ScreenView(BoardComposeArt.Instance).Set(BoardComposeArt.Title, reply ? "REPLY TO A MESSAGE" : "NEW MESSAGE");
        if (!session.IsMember)
        {
            view.Set(BoardComposeArt.Status, Ui.AccessDeniedText(session));
            await Term.TransitionAsync(Transition.Wipe, view.Render(), cancellationToken);
            await view.PressAnyKeyAsync(Term, cancellationToken);
            return;
        }

        long? parent = null;
        if (reply)
        {
            view.Set(BoardComposeArt.Prompt, "Reply to message #:");
            await Term.TransitionAsync(Transition.Wipe, view.Render(), cancellationToken);
            await Term.WriteAsync(view.MoveTo(BoardComposeArt.Number), cancellationToken);
            var number = await LineEditor.ReadLineAsync(Term, 9, string.Empty, cancellationToken);
            if (!long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || messages.All(m => m.Id != id))
            {
                await FinishAsync(view, "|09No such message on this wall.|07", cancellationToken);
                return;
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
            await FinishAsync(view, "|08Message abandoned. The wall forgives you.|07", cancellationToken);
            return;
        }

        var result = await Context.Api.PostMessageAsync(new PostMessageRequest(parent, body), cancellationToken);
        await FinishAsync(view, result.IsOk ? "|10MESSAGE QUEUED FOR SYSOP REVIEW.|08 It appears once approved.|07" : Ui.FailureText(result), cancellationToken);
    }

    private async Task FinishAsync(ScreenView view, string status, CancellationToken cancellationToken)
    {
        await view.UpdateAsync(Term, BoardComposeArt.Status, status, cancellationToken);
        await view.PressAnyKeyAsync(Term, cancellationToken);
    }
}
