using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Components;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// One bulletin (<c>/bulletins/{n}</c>), a page at a time. A bulletin with an animation (the SysOp cam) uses
/// the cam art: the cam shows its first frame as the bulletin draws and plays once the last page is showing.
/// An unknown number shows "file not found".
/// </summary>
public sealed partial class BulletinPage
{
    /// <summary>The cam's box: the cam art's <c>@ANIM:CAM@</c> slot.</summary>
    public static readonly AnimationOptions CamBox = new(
        Row: BulletinCamArt.Cam.Row,
        Column: BulletinCamArt.Cam.Column,
        Width: BulletinCamArt.Cam.Width,
        Height: BulletinCamArt.Cam.Height,
        Loop: true,
        MaxDuration: TimeSpan.FromSeconds(20));

    /// <summary>The prompt while the cam plays.</summary>
    public const string CamLive = "|08(the SysOp cam is live - any key stops it)|07";

    private bool loaded;
    private Bulletin? bulletin;
    private IReadOnlyList<string> lines = [];
    private string? prompt;
    private BbsScreen? screen;

    /// <summary>Gets or sets the content store.</summary>
    [Inject]
    public required ContentStore Content { get; set; }

    /// <summary>Gets or sets the bulletin number from the address.</summary>
    [Parameter]
    public int Number { get; set; }

    private bool Cam => bulletin?.Animation is not null;

    private ScreenArt Art => Cam ? BulletinCamArt.Instance : BulletinArt.Instance;

    private FieldSlot Body => Cam ? BulletinCamArt.Body : BulletinArt.Body;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var board = await Content.GetBulletinsAsync();
        bulletin = board.Bulletins.FirstOrDefault(b => b.Number == Number);
        lines = bulletin is null ? [] : bulletin.Lines.SelectMany(l => TextLayout.Wrap(l, Body.Width)).ToList();
        prompt = null;
        loaded = true;
    }

    private async Task ShowCamAsync()
    {
        if (bulletin?.Animation is { } animation)
        {
            await Host.Terminal.PlayAnimationAsync(animation, CamBox with { MaxDuration = TimeSpan.FromMilliseconds(1) }, Host.PageToken);
        }
    }

    private async Task LastPageAsync()
    {
        if (bulletin?.Animation is { } animation)
        {
            prompt = CamLive;
            screen?.ShowPrompt(CamLive);
            await Host.Terminal.PlayAnimationAsync(animation, CamBox, Host.PageToken);
        }

        prompt = Ui.AnyKey;
    }
}
