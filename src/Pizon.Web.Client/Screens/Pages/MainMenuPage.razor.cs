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

    /// <summary>
    /// Maps a menu key to its screen for this caller, the same way the menu's hotkeys do (the RIP card's
    /// buttons send menu keys).
    /// </summary>
    /// <param name="key">The key pressed.</param>
    /// <param name="session">The call (some keys depend on who is calling).</param>
    /// <returns>The screen, or <see langword="null"/> for keys that do nothing.</returns>
    public static ScreenId? Route(string? key, BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (key is not { Length: 1 })
        {
            return null;
        }

        return char.ToUpperInvariant(key[0]) switch
        {
            '1' => ScreenId.Bulletins,
            '2' => ScreenId.MessageBoard,
            '3' => ScreenId.FileAreas,
            '4' => ScreenId.Doors,
            '5' => ScreenId.UserList,
            'P' => ScreenId.PageSysop,
            'H' => ScreenId.ChangeHandle,
            'S' => ScreenId.Settings,
            'R' => ScreenId.RipCard,
            'G' => ScreenId.Goodbye,
            'D' when !session.IsMember && session.Me.DiscordEnabled => ScreenId.SignIn,
            'L' when session.Me.Authenticated => ScreenId.Logout,
            '*' when session.IsSysop => ScreenId.SysopConsole,
            _ => null,
        };
    }

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
