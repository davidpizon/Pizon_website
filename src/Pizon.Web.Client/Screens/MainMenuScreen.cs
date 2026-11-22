using System.Globalization;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// The main menu: draws itself behind a moving cursor, shows callers online on row 25 and routes keys.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class MainMenuScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.MainMenu;

    /// <summary>
    /// Maps a menu key to its screen for this caller.
    /// </summary>
    /// <param name="key">The key pressed.</param>
    /// <param name="session">The call (some keys depend on who is calling).</param>
    /// <returns>The screen, or <see langword="null"/> for keys that do nothing.</returns>
    public static ScreenId? Route(string key, BbsSession session)
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

    /// <summary>
    /// Binds the menu art for a caller: the items that depend on who is calling, the handle and level.
    /// </summary>
    /// <param name="session">The call.</param>
    /// <returns>The bound view (the callers-online row is left empty).</returns>
    public static ScreenView View(BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new ScreenView(MainMenuArt.Instance)
            .Set(MainMenuArt.ItemAccount, session.IsMember ? Ui.ItemPipe("H", "Change Handle") : Ui.ItemPipe("D", "Sign in with Discord"))
            .Set(MainMenuArt.ItemLogout, session.Me.Authenticated ? Ui.ItemPipe("L", "Log out of Discord") : string.Empty)
            .Set(MainMenuArt.ItemSysop, session.IsSysop ? Ui.ItemPipe("*", "|09SysOp Console") : string.Empty)
            .Set(MainMenuArt.Handle, PipeCodes.Escape(session.Handle))
            .Set(MainMenuArt.Level, session.Level.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Builds the menu screen for a caller.
    /// </summary>
    /// <param name="session">The call.</param>
    /// <returns>ANSI text for the whole menu screen (without the online line).</returns>
    public static string Render(BbsSession session) => View(session).Render();

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var view = View(session).Set(MainMenuArt.Online, Ui.OnlineText(Context.Presence));
        await Term.TransitionAsync(Transition.Draw, view.Render(), cancellationToken);

        void OnPresenceChanged() => _ = view.UpdateAsync(Term, MainMenuArt.Online, Ui.OnlineText(Context.Presence), CancellationToken.None);
        Context.Presence.Changed += OnPresenceChanged;
        try
        {
            while (true)
            {
                var key = await Term.ReadKeyAsync(cancellationToken);
                var next = Route(key, session);
                if (next is { } screen)
                {
                    await Term.WriteAsync(key.ToUpperInvariant(), cancellationToken);
                    await Term.TransitionAsync(Transition.Undraw, Ui.Cls, cancellationToken);
                    return screen;
                }

                if (key == DoorKeys.Escape)
                {
                    await Term.WriteAsync(view.CursorHome() + Ui.P("|08(To log off, press |14G|08.)|07") + view.CursorHome(), cancellationToken);
                }
            }
        }
        finally
        {
            Context.Presence.Changed -= OnPresenceChanged;
        }
    }
}
