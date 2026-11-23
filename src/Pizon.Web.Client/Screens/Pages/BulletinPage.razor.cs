using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Services;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// One bulletin (<c>/bulletins/{n}</c>), a page at a time. A bulletin with the SysOp cam uses the cam art,
/// whose <c>@ANIM:CAM@</c> slot plays <see cref="SysopCamArt"/> while you read: its idle frames, or its busy
/// ones while other callers are online. An unknown number shows "file not found".
/// </summary>
public sealed partial class BulletinPage
{
    private bool loaded;
    private Bulletin? bulletin;
    private IReadOnlyList<string> lines = [];
    private string? prompt;

    /// <summary>Gets or sets the content store.</summary>
    [Inject]
    public required ContentStore Content { get; set; }

    /// <summary>Gets or sets who else is online (the cam looks busy when anyone is).</summary>
    [Inject]
    public required IPresence Presence { get; set; }

    /// <summary>Gets or sets the bulletin number from the address.</summary>
    [Parameter]
    public int Number { get; set; }

    /// <summary>
    /// Picks the cam's frames: busy while anyone else is online, idle otherwise.
    /// </summary>
    /// <param name="online">How many callers are online, this one included.</param>
    /// <returns>A range of <see cref="SysopCamArt"/>.</returns>
    public static string CamRange(int online) => online > 1 ? SysopCamArt.Ranges.Busy : SysopCamArt.Ranges.Idle;

    private bool Cam => bulletin?.Cam == true;

    private ScreenArt Art => Cam ? BulletinCamArt.Instance : BulletinArt.Instance;

    private FieldSlot Body => Cam ? BulletinCamArt.Body : BulletinArt.Body;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Presence.Changed += OnPresenceChanged;
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var board = await Content.GetBulletinsAsync();
        bulletin = board.Bulletins.FirstOrDefault(b => b.Number == Number);
        lines = bulletin is null ? [] : bulletin.Lines.SelectMany(l => TextLayout.Wrap(l, Body.Width)).ToList();
        prompt = null;
        loaded = true;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Presence.Changed -= OnPresenceChanged;
        }

        base.Dispose(disposing);
    }

    private void OnPresenceChanged() => _ = InvokeAsync(StateHasChanged);

    private void LastPage() => prompt = Ui.AnyKey;
}
