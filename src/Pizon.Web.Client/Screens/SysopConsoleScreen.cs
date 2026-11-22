using System.Globalization;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Rendering;
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
    /// Binds one pending message into the moderation queue art.
    /// </summary>
    /// <param name="message">The pending message.</param>
    /// <param name="index">Its position (1-based).</param>
    /// <param name="total">How many are waiting.</param>
    /// <returns>The bound view.</returns>
    public static ScreenView PendingView(PendingMessageDto message, int index, int total)
    {
        ArgumentNullException.ThrowIfNull(message);
        var reply = message.ParentId is { } p ? string.Create(CultureInfo.InvariantCulture, $" · reply to #{p}") : string.Empty;
        var head = string.Create(
            CultureInfo.InvariantCulture,
            $"|08[{index}/{total}] |14#{message.Id}|08 from |15{PipeCodes.Escape(message.Handle)}|08 (user {message.UserId}) · {Ui.Stamp(message.PostedAt)}{reply}|07");
        return new ScreenView(SysopQueueArt.Instance)
            .Set(SysopQueueArt.Head, head)
            .Set(SysopQueueArt.Body, PipeCodes.Escape(message.Body));
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
            await Term.TransitionAsync(Transition.Wipe, new ScreenView(SysopArt.Instance).Render(), cancellationToken);
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
            var empty = new ScreenView(SysopPagesArt.Instance)
                .Set(SysopPagesArt.Title, "MODERATION QUEUE")
                .Set(SysopPagesArt.Body, queue.IsOk ? "|08  Nothing waiting. Go touch grass.|07" : Ui.FailureText(queue));
            await Term.WriteAsync(empty.Render(), cancellationToken);
            await empty.PressAnyKeyAsync(Term, cancellationToken);
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var m = items[i];
            var view = PendingView(m, i + 1, items.Count);
            await Term.WriteAsync(view.Render(), cancellationToken);
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
                    await view.UpdateAsync(Term, SysopQueueArt.Status, Ui.FailureText(result), cancellationToken);
                    await view.PressAnyKeyAsync(Term, cancellationToken);
                }

                break;
            }
        }
    }

    private async Task PagesAsync(CancellationToken cancellationToken)
    {
        var view = new ScreenView(SysopPagesArt.Instance).Set(SysopPagesArt.Title, "PAGES INBOX");
        await Term.WriteAsync(view.Render(), cancellationToken);
        var pages = await Context.Api.GetPagesAsync(cancellationToken);
        var items = pages.Value ?? [];
        var lines = items.SelectMany(p => new[]
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"  {(p.Read ? "|08 " : "|10*")} |14#{p.Id}|08 · |15{PipeCodes.Escape(p.Handle)}|08 · {Ui.Stamp(p.SentAt)}|07"),
            $"      |07{PipeCodes.Escape(p.Message)}|07",
        }).ToList();
        if (!pages.IsOk)
        {
            lines.Add(Ui.FailureText(pages));
        }
        else if (items.Count == 0)
        {
            lines.Add("|08  No pages. Nobody needs you. Enjoy it.|07");
        }

        await view.PageAsync(Term, SysopPagesArt.Body, SysopPagesArt.More, lines, cancellationToken);
        foreach (var p in items.Where(p => !p.Read))
        {
            _ = await Context.Api.MarkPageReadAsync(p.Id, cancellationToken);
        }

        await view.PressAnyKeyAsync(Term, cancellationToken);
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
