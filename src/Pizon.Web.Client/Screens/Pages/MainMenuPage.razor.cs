using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk;
using Pizon.Web.Client.Screens.Components;
using Pizon.Web.Client.Services;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// The main menu (<c>/menu</c>): drawn behind a moving cursor, with the items that depend on who is calling
/// and a live callers-online line on row 25, which redraws by itself whenever presence changes.
/// </summary>
public sealed partial class MainMenuPage
{
    /// <summary>The hint shown when Escape is pressed on the menu.</summary>
    public const string EscapeHint = "|08(To log off, press |14G|08.)|07";

    private BbsScreen? screen;

    /// <summary>Gets or sets presence.</summary>
    [Inject]
    public required IPresence Presence { get; set; }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Presence.Changed += OnPresenceChanged;
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

    private void OnKey(string key)
    {
        if (key == DoorKeys.Escape)
        {
            screen?.SayAtCursor(EscapeHint);
        }
    }
}
