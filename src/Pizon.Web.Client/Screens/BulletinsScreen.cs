using System.Globalization;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// [1] System Bulletins: SysOp announcements (the /now list, the bio) plus a Top Players bulletin built
/// from the door score boards.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class BulletinsScreen(ScreenContext context) : ScreenBase(context)
{
    /// <summary>The box used for a bulletin's animation (the SysOp cam): the art's <c>@ANIM:CAM@</c> slot.</summary>
    public static readonly AnimationOptions CamBox = new(
        Row: BulletinCamArt.Cam.Row,
        Column: BulletinCamArt.Cam.Column,
        Width: BulletinCamArt.Cam.Width,
        Height: BulletinCamArt.Cam.Height,
        Loop: true,
        MaxDuration: TimeSpan.FromSeconds(20));

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
        var items = board.Bulletins.Select(b =>
        {
            var n = b.Number.ToString(CultureInfo.InvariantCulture);
            var updated = string.IsNullOrEmpty(b.Updated) ? string.Empty : $"  |08(updated {b.Updated})|07";
            return Ui.ItemPipe(n, $"Bulletin #{n}: |15{b.Title}") + updated;
        });
        return new ScreenView(BulletinsArt.Instance).SetLines(BulletinsArt.List, items).Render();
    }

    private async Task ShowBulletinAsync(Bulletin bulletin, CancellationToken cancellationToken)
    {
        var cam = bulletin.Animation is not null;
        var view = new ScreenView(cam ? BulletinCamArt.Instance : BulletinArt.Instance);
        var body = cam ? BulletinCamArt.Body : BulletinArt.Body;
        var more = cam ? BulletinCamArt.More : BulletinArt.More;
        view.Set(cam ? BulletinCamArt.Title : BulletinArt.Title, $"BULLETIN #{bulletin.Number}: {bulletin.Title.ToUpperInvariant()}");
        await Term.TransitionAsync(Transition.Wipe, view.Render(), cancellationToken);
        if (cam)
        {
            _ = await Term.PlayAnimationAsync(bulletin.Animation!, CamBox with { MaxDuration = TimeSpan.FromMilliseconds(1) }, cancellationToken);
        }

        var lines = bulletin.Lines.SelectMany(l => TextLayout.Wrap(l, body.Width)).ToList();
        await view.PageAsync(Term, body, more, lines, cancellationToken);
        if (cam)
        {
            await view.UpdateAsync(Term, more, "|08(the SysOp cam is live - any key stops it)|07", cancellationToken);
            await Term.PlayAnimationAsync(bulletin.Animation!, CamBox, cancellationToken);
            await view.UpdateAsync(Term, more, string.Empty, cancellationToken);
        }

        await view.PressAnyKeyAsync(Term, cancellationToken);
    }

    private async Task ShowTopPlayersAsync(CancellationToken cancellationToken)
    {
        var view = new ScreenView(TopPlayersArt.Instance);
        await Term.TransitionAsync(Transition.Wipe, view.Render(), cancellationToken);
        var manifest = await Context.Content.GetDoorsAsync(cancellationToken);
        var day = (await Term.GetLocalDateAsync()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lines = new List<string>();
        foreach (var door in manifest.EnabledDoors.Where(d => d.Scored))
        {
            var result = await Context.Api.GetScoresAsync(door.Id, day, cancellationToken);
            lines.Add($"|13{door.Title} - {door.ScoreTitle}|07");
            var entries = result.Value ?? [];
            if (entries.Count == 0)
            {
                lines.Add("|08  Nobody yet. Be the first.|07");
            }

            lines.AddRange(entries.Select(e => string.Create(
                CultureInfo.InvariantCulture,
                $"  |14{e.Rank,2}.|07 {PipeCodes.Escape(TextWrap.Fit(e.Handle, 16))} |11{e.Score,8:N0}|07")));
            lines.Add(string.Empty);
        }

        if (lines.Count == 0)
        {
            lines.Add("|08No scored doors are open right now.|07");
        }

        await view.PageAsync(Term, TopPlayersArt.Body, TopPlayersArt.More, lines, cancellationToken);
        await view.PressAnyKeyAsync(Term, cancellationToken);
    }
}
