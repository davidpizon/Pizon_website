using System.Text;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
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
    /// Builds the menu text for a caller.
    /// </summary>
    /// <param name="session">The call.</param>
    /// <returns>ANSI text for the whole menu screen (without the online line).</returns>
    public static string Render(BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var sb = new StringBuilder();
        sb.Append(Ui.Cls).Append(Ui.Header("Main menu"));
        sb.Append(Ui.P($"|#039   ╔{new string('═', 64)}╗\r\n"));
        AppendRow(sb, Ui.Item("1", "System Bulletins"), Ui.Item("P", "Page the SysOp"));
        AppendRow(sb, Ui.Item("2", "Message Boards"), session.IsMember ? Ui.Item("H", "Change Handle") : Ui.Item("D", "Sign in with Discord"));
        AppendRow(sb, Ui.Item("3", "File Areas"), Ui.Item("S", "Terminal Settings"));
        AppendRow(sb, Ui.Item("4", "Door Games"), Ui.Item("R", "RIP Welcome Card"));
        AppendRow(sb, Ui.Item("5", "User List & Hall of Fame"), session.Me.Authenticated ? Ui.Item("L", "Log out of Discord") : string.Empty);
        AppendRow(sb, string.Empty, Ui.Item("G", Ui.P("|13Goodbye - Logoff|07")));
        if (session.IsSysop)
        {
            AppendRow(sb, Ui.Item("*", Ui.P("|09SysOp Console|07")), string.Empty);
        }

        sb.Append(Ui.P($"|#039   ╚{new string('═', 64)}╝|07\r\n\r\n"));
        sb.Append(Ui.P($"|08   Connected as |14{PipeCodes.Escape(session.Handle)}|08 (Lvl {session.Level}) · Press a key, or click an option.|07\r\n\r\n"));
        sb.Append(Ui.P("|11   Your choice: |07"));
        return sb.ToString();
    }

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await Term.TransitionAsync(Transition.Draw, Render(session), cancellationToken);
        await Term.WriteAsync(Ui.OnlineLine(Context.Presence), cancellationToken);

        void OnPresenceChanged() => _ = Term.WriteAsync(Ui.OnlineLine(Context.Presence), CancellationToken.None);
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
                    await SayAsync("\r\n|08   (To log off, press |14G|08.)|07", cancellationToken);
                }
            }
        }
        finally
        {
            Context.Presence.Changed -= OnPresenceChanged;
        }
    }

    private static void AppendRow(StringBuilder sb, string left, string right)
    {
        var leftPlain = PipeCodes.VisibleLength(left);
        var rightPlain = PipeCodes.VisibleLength(right);
        sb.Append(Ui.P("|#039   ║ "))
            .Append(left).Append(' ', Math.Max(1, 32 - leftPlain))
            .Append(right).Append(' ', Math.Max(0, 31 - rightPlain))
            .Append(Ui.P("|#039║|07\r\n"));
    }
}
