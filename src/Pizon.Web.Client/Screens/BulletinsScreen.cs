using System.Globalization;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// [1] System Bulletins: SysOp announcements (the /now list, the bio) plus a Top Players bulletin built
/// from the door score boards.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class BulletinsScreen(ScreenContext context) : ScreenBase(context)
{
    /// <summary>The box used for a bulletin's animation (the SysOp cam), right of the text.</summary>
    public static readonly AnimationOptions CamBox = new(Row: 4, Column: 54, Width: 24, Height: 12, Loop: true, MaxDuration: TimeSpan.FromSeconds(20));

    /// <inheritdoc />
    public override ScreenId Id => ScreenId.Bulletins;

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        var board = await Context.Content.GetBulletinsAsync(cancellationToken);
        while (true)
        {
            await Term.TransitionAsync(Transition.Wipe, RenderList(board), cancellationToken);
            var key = await Term.ReadKeyAsync(cancellationToken);
            if (DoorKeys.Is(key, 'Q') || key == DoorKeys.Escape)
            {
                return ScreenId.MainMenu;
            }

            if (DoorKeys.Is(key, 'T'))
            {
                await ShowTopPlayersAsync(cancellationToken);
                continue;
            }

            var bulletin = board.Bulletins.FirstOrDefault(b => b.Number.ToString(CultureInfo.InvariantCulture) == key);
            if (bulletin is not null)
            {
                await ShowBulletinAsync(bulletin, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Builds the bulletin list.
    /// </summary>
    /// <param name="board">The bulletins.</param>
    /// <returns>ANSI text for the list screen.</returns>
    public static string RenderList(BulletinBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var text = Ui.Cls + Ui.Header("System bulletins") + Ui.P("|08   Notices from the SysOp. Read them; there may be a quiz (there won't).|07\r\n\r\n");
        foreach (var b in board.Bulletins)
        {
            var updated = string.IsNullOrEmpty(b.Updated) ? string.Empty : Ui.P($"  |08(updated {b.Updated})|07");
            text += "   " + Ui.Item(b.Number.ToString(CultureInfo.InvariantCulture), Ui.P($"Bulletin #{b.Number}: |15{b.Title}|07")) + updated + "\r\n";
        }

        text += "   " + Ui.Item("T", "Top Players (door game scores)") + "\r\n\r\n";
        text += "   " + Ui.Item("Q", "Back to the main menu") + "\r\n\r\n" + Ui.P("|11   Read which bulletin? |07");
        return text;
    }

    private async Task ShowBulletinAsync(Bulletin bulletin, CancellationToken cancellationToken)
    {
        var heading = Ui.Cls + Ui.Header($"Bulletin #{bulletin.Number}: {bulletin.Title}");
        await Term.TransitionAsync(Transition.Wipe, heading, cancellationToken);
        var width = bulletin.Animation is null ? 76 : 50;
        var lines = bulletin.Lines
            .SelectMany(l => WrapPipeLine(l, width))
            .Select(l => "  " + Ui.P(l + "|07"))
            .ToList();
        if (bulletin.Animation is not null)
        {
            _ = await Term.PlayAnimationAsync(bulletin.Animation, CamBox with { MaxDuration = TimeSpan.FromMilliseconds(1) }, cancellationToken);
            await Term.WriteAsync(Ansi.MoveTo(5, 1), cancellationToken);
        }

        await Ui.PageAsync(Term, lines, 17, cancellationToken);
        if (bulletin.Animation is not null)
        {
            await SayAsync("\r\n|08  (the SysOp cam is live - any key stops it)|07", cancellationToken);
            await Term.PlayAnimationAsync(bulletin.Animation, CamBox, cancellationToken);
        }

        await Ui.PressAnyKeyAsync(Term, cancellationToken);
    }

    private async Task ShowTopPlayersAsync(CancellationToken cancellationToken)
    {
        await Term.TransitionAsync(Transition.Wipe, Ui.Cls + Ui.Header("Top players"), cancellationToken);
        var manifest = await Context.Content.GetDoorsAsync(cancellationToken);
        var day = (await Term.GetLocalDateAsync()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var any = false;
        foreach (var door in manifest.EnabledDoors.Where(d => d.Scored))
        {
            any = true;
            var result = await Context.Api.GetScoresAsync(door.Id, day, cancellationToken);
            await SayAsync($"|13  {door.Title} - {door.ScoreTitle}|07\r\n", cancellationToken);
            var entries = result.Value ?? [];
            if (entries.Count == 0)
            {
                await SayAsync("|08    Nobody yet. Be the first.|07\r\n\r\n", cancellationToken);
                continue;
            }

            foreach (var e in entries)
            {
                await SayAsync(
                    string.Create(CultureInfo.InvariantCulture, $"    |14{e.Rank,2}.|07 {PipeCodes.Escape(TextWrap.Fit(e.Handle, 16))} |11{e.Score,8:N0}|07\r\n"),
                    cancellationToken);
            }

            await SayAsync("\r\n", cancellationToken);
        }

        if (!any)
        {
            await SayAsync("|08  No scored doors are open right now.|07\r\n", cancellationToken);
        }

        await Ui.PressAnyKeyAsync(Term, cancellationToken);
    }

    /// <summary>
    /// Wraps a line that may contain pipe codes, measuring only the visible characters.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="width">The visible width.</param>
    /// <returns>The wrapped lines (color codes carried at the start of each piece where they appear).</returns>
    public static IEnumerable<string> WrapPipeLine(string line, int width)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (PipeCodes.VisibleLength(line) <= width)
        {
            return [line];
        }

        var pieces = new List<string>();
        var current = string.Empty;
        var visible = 0;
        foreach (var word in line.Split(' '))
        {
            var w = PipeCodes.VisibleLength(word);
            if (visible > 0 && visible + 1 + w > width)
            {
                pieces.Add(current);
                current = word;
                visible = w;
            }
            else
            {
                current = visible == 0 && current.Length == 0 ? word : current + " " + word;
                visible += (visible == 0 ? 0 : 1) + w;
            }
        }

        pieces.Add(current);
        return pieces;
    }
}
