using System.Globalization;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// [*] The SysOp console: the moderation queue (approve, reject, ban), the pages inbox and message deletion.
/// The server enforces SysOp access; this screen only hides itself from everyone else.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class SysopConsoleScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.SysopConsole;

    /// <summary>
    /// Formats one pending message for review.
    /// </summary>
    /// <param name="message">The pending message.</param>
    /// <param name="index">Its position (1-based).</param>
    /// <param name="total">How many are waiting.</param>
    /// <returns>ANSI text.</returns>
    public static string RenderPending(PendingMessageDto message, int index, int total)
    {
        ArgumentNullException.ThrowIfNull(message);
        var reply = message.ParentId is { } p ? string.Create(CultureInfo.InvariantCulture, $" · reply to #{p}") : string.Empty;
        var head = Ui.P(string.Create(
            CultureInfo.InvariantCulture,
            $"|08  [{index}/{total}] |14#{message.Id}|08 from |15{PipeCodes.Escape(message.Handle)}|08 (user {message.UserId}) · {Ui.Stamp(message.PostedAt)}{reply}|07\r\n\r\n"));
        var body = string.Concat(TextWrap.Wrap(message.Body, 74).Select(l => Ui.P($"    |07{PipeCodes.Escape(l)}\r\n")));
        return head + body + "\r\n  " + Ui.Item("A", "Approve") + "  " + Ui.Item("X", "Reject") + "  " +
            Ui.Item("B", Ui.P("|09Ban author|07")) + "  " + Ui.Item("N", "Skip") + "  " + Ui.Item("Q", "Done") + "\r\n";
    }

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsSysop)
        {
            return ScreenId.MainMenu;
        }

        while (true)
        {
            await Term.TransitionAsync(
                Transition.Wipe,
                Ui.Cls + Ui.Header("SysOp console") +
                "   " + Ui.Item("1", "Moderation queue") + "\r\n" +
                "   " + Ui.Item("2", "Pages inbox") + "\r\n" +
                "   " + Ui.Item("3", "Delete a message") + "\r\n\r\n" +
                "   " + Ui.Item("Q", "Back to the main menu") + Ui.P("\r\n\r\n|11   SysOp command: |07"),
                cancellationToken);
            var key = await Term.ReadKeyAsync(cancellationToken);
            switch (key == DoorKeys.Escape ? key : key.ToUpperInvariant())
            {
                case "1":
                    await QueueAsync(cancellationToken);
                    break;
                case "2":
                    await PagesAsync(cancellationToken);
                    break;
                case "3":
                    await DeleteAsync(cancellationToken);
                    break;
                case "Q":
                case DoorKeys.Escape:
                    return ScreenId.MainMenu;
                default:
                    break;
            }
        }
    }

    private async Task QueueAsync(CancellationToken cancellationToken)
    {
        var queue = await Context.Api.GetModerationQueueAsync(cancellationToken);
        var items = queue.Value ?? [];
        if (!queue.IsOk || items.Count == 0)
        {
            await Term.WriteAsync(Ui.Cls + Ui.Header("Moderation queue") + (queue.IsOk ? Ui.P("|08  Nothing waiting. Go touch grass.|07\r\n") : Ui.Failure(queue)), cancellationToken);
            await Ui.PressAnyKeyAsync(Term, cancellationToken);
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var m = items[i];
            await Term.WriteAsync(Ui.Cls + Ui.Header("Moderation queue") + RenderPending(m, i + 1, items.Count), cancellationToken);
            while (true)
            {
                var key = await Term.ReadKeyAsync(cancellationToken);
                ApiResult? result = null;
                if (DoorKeys.Is(key, 'A') || DoorKeys.Is(key, 'X'))
                {
                    result = await Context.Api.ModerateAsync(m.Id, DoorKeys.Is(key, 'A'), cancellationToken);
                }
                else if (DoorKeys.Is(key, 'B'))
                {
                    result = await Context.Api.BanUserAsync(m.UserId, cancellationToken);
                    if (result.IsOk)
                    {
                        result = await Context.Api.ModerateAsync(m.Id, approve: false, cancellationToken);
                    }
                }
                else if (DoorKeys.Is(key, 'N'))
                {
                    break;
                }
                else if (DoorKeys.Is(key, 'Q') || key == DoorKeys.Escape)
                {
                    return;
                }
                else
                {
                    continue;
                }

                if (!result.IsOk)
                {
                    await Term.WriteAsync(Ui.Failure(result), cancellationToken);
                    await Ui.PressAnyKeyAsync(Term, cancellationToken);
                }

                break;
            }
        }
    }

    private async Task PagesAsync(CancellationToken cancellationToken)
    {
        var pages = await Context.Api.GetPagesAsync(cancellationToken);
        await Term.WriteAsync(Ui.Cls + Ui.Header("Pages inbox"), cancellationToken);
        var items = pages.Value ?? [];
        if (!pages.IsOk)
        {
            await Term.WriteAsync(Ui.Failure(pages), cancellationToken);
        }
        else if (items.Count == 0)
        {
            await SayAsync("|08  No pages. Nobody needs you. Enjoy it.|07\r\n", cancellationToken);
        }

        var lines = items.SelectMany(p => new[]
        {
            Ui.P(string.Create(
                CultureInfo.InvariantCulture,
                $"  {(p.Read ? "|08 " : "|10*")} |14#{p.Id}|08 · |15{PipeCodes.Escape(p.Handle)}|08 · {Ui.Stamp(p.SentAt)}|07")),
            Ui.P($"      |07{PipeCodes.Escape(p.Message)}|07"),
        }).ToList();
        await Ui.PageAsync(Term, lines, 18, cancellationToken);
        foreach (var p in items.Where(p => !p.Read))
        {
            _ = await Context.Api.MarkPageReadAsync(p.Id, cancellationToken);
        }

        await Ui.PressAnyKeyAsync(Term, cancellationToken);
    }

    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        await SayAsync("\r\n|11   Delete message #: |07", cancellationToken);
        var text = await LineEditor.ReadLineAsync(Term, 9, string.Empty, cancellationToken);
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return;
        }

        await SayAsync($"\r\n|09   Delete #{id} and all its replies? This cannot be undone. |14[Y/N]|07 ", cancellationToken);
        if (!DoorKeys.Is(await Term.ReadKeyAsync(cancellationToken), 'Y'))
        {
            return;
        }

        var result = await Context.Api.DeleteMessageAsync(id, cancellationToken);
        await Term.WriteAsync(result.IsOk ? Ui.P("\r\n|10   Deleted. It never happened.|07\r\n") : Ui.Failure(result), cancellationToken);
        await Ui.PressAnyKeyAsync(Term, cancellationToken);
    }
}
