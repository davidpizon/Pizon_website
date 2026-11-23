using Pizon.Doors.Sdk;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// [R] The RIPscrip welcome card (<c>/rip</c>): a 640x350 vector logo with a clickable button bar. Its
/// buttons send main menu keys, which go where the menu's would.
/// </summary>
public sealed partial class RipPage
{
    /// <summary>The welcome card URL.</summary>
    public const string CardUrl = "content/welcome.rip";

    /// <summary>
    /// Maps a main menu key from the card to an address, the way the menu's hotkeys do.
    /// </summary>
    /// <param name="key">The key the card sent.</param>
    /// <param name="session">The call (the SysOp console is only for the SysOp).</param>
    /// <returns>The address, or <see langword="null"/> for keys that go back to the main menu.</returns>
    public static string? Route(string? key, BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (key is not { Length: 1 })
        {
            return null;
        }

        return char.ToUpperInvariant(key[0]) switch
        {
            '1' => "/bulletins",
            '2' => "/board",
            '3' => "/files",
            '4' => "/doors",
            '5' => "/users",
            'P' => "/page",
            'H' => "/handle",
            'S' => "/settings",
            'G' => "/goodbye",
            '*' when session.IsSysop => "/sysop",
            _ => null,
        };
    }

    private async Task ShowCardAsync()
    {
        var key = await Host.Terminal.ShowRipAsync(CardUrl, Host.PageToken);
        if (DoorKeys.Is(key, 'D') && !Session.IsMember && Session.Me.DiscordEnabled)
        {
            await Host.SignInAsync();
        }
        else if (DoorKeys.Is(key, 'L') && Session.Me.Authenticated)
        {
            await Host.LogOutAsync();
        }
        else
        {
            Host.Navigate(Route(key, Session) ?? "/menu");
        }
    }
}
