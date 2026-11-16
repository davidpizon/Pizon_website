using System.Globalization;
using System.Text;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// [4] Door Games: the door list from <c>doors.json</c>, with locks on doors that need sign-in.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class DoorsScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.Doors;

    /// <summary>
    /// Builds the door list.
    /// </summary>
    /// <param name="doors">The enabled doors.</param>
    /// <param name="session">The call (for lock markers).</param>
    /// <returns>ANSI text.</returns>
    public static string Render(IReadOnlyList<DoorManifestEntry> doors, BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(session);
        var sb = new StringBuilder(Ui.Cls + Ui.Header("Door games"));
        sb.Append(Ui.P("|08   External programs. The BBS steps aside while you play.|07\r\n\r\n"));
        if (doors.Count == 0)
        {
            sb.Append(Ui.P("|08   All doors are closed for repairs. The SysOp is holding a screwdriver.|07\r\n"));
        }

        for (var i = 0; i < doors.Count && i < 9; i++)
        {
            var d = doors[i];
            var locked = d.RequiresAuth && !session.IsMember;
            var tag = locked ? Ui.P(" |09[LOCKED - LVL 10]|07") : d.Scored ? Ui.P(" |13[SCORED]|07") : string.Empty;
            sb.Append("   ").Append(Ui.Item((i + 1).ToString(CultureInfo.InvariantCulture), Ui.P($"|15{PipeCodes.Escape(d.Title)}|07"))).Append(tag).Append("\r\n");
            sb.Append(Ui.P($"|08       {PipeCodes.Escape(TextWrap.Fit(d.Description, 68))}|07\r\n"));
        }

        sb.Append("\r\n   ").Append(Ui.Item("Q", "Back to the main menu")).Append(Ui.P("\r\n\r\n|11   Open which door? |07"));
        return sb.ToString();
    }

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var manifest = await Context.Content.GetDoorsAsync(cancellationToken);
        var doors = manifest.EnabledDoors.ToList();
        while (true)
        {
            await Term.TransitionAsync(Transition.Wipe, Render(doors, session), cancellationToken);
            var key = await Term.ReadKeyAsync(cancellationToken);
            if (DoorKeys.Is(key, 'Q') || key == DoorKeys.Escape)
            {
                return ScreenId.MainMenu;
            }

            if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= doors.Count)
            {
                await Context.Doors.RunAsync(doors[n - 1], session, cancellationToken);
            }
        }
    }
}

/// <summary>
/// [5] User List and Hall of Fame: recent signed-in callers plus the SysOp's hand-picked legends.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class UserListScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.UserList;

    /// <summary>
    /// Builds the hall of fame lines.
    /// </summary>
    /// <param name="fame">The hall of fame.</param>
    /// <returns>ANSI lines.</returns>
    public static IReadOnlyList<string> RenderFame(HallOfFame fame)
    {
        ArgumentNullException.ThrowIfNull(fame);
        var lines = new List<string> { Ui.P("|13  HALL OF FAME|08 - legends of the board|07"), string.Empty };
        foreach (var e in fame.Entries)
        {
            var handle = Ui.P($"|15{TextWrap.Fit(e.Handle, 16)}|07");
            if (e.Url is not null)
            {
                handle = Ansi.LinkStart(e.Url) + handle + Ansi.LinkEnd();
            }

            lines.Add("   " + handle + Ui.P($" |11{TextWrap.Fit(e.Clearance, 9)}|07 {PipeCodes.Escape(TextWrap.Fit(e.Note, 48))}"));
        }

        return lines;
    }

    /// <summary>
    /// Builds the recent callers lines.
    /// </summary>
    /// <param name="callers">Recent callers.</param>
    /// <returns>ANSI lines.</returns>
    public static IReadOnlyList<string> RenderCallers(IReadOnlyList<CallerDto> callers)
    {
        ArgumentNullException.ThrowIfNull(callers);
        var lines = new List<string> { Ui.P("|13  LAST CALLERS|08 - signed-in callers only; guests leave no trace|07"), string.Empty };
        if (callers.Count == 0)
        {
            lines.Add(Ui.P("|08   Nobody has signed the log yet.|07"));
        }

        foreach (var c in callers)
        {
            lines.Add(Ui.P(string.Create(
                CultureInfo.InvariantCulture,
                $"   |15{PipeCodes.Escape(TextWrap.Fit(c.Handle, 16))}|07 |11LVL {c.Level,3}|07  |08last call {Ui.Stamp(c.LastSeen)}|07")));
        }

        return lines;
    }

    /// <summary>
    /// Builds the credits lines, including the attribution the CC BY-SA terminal font requires.
    /// </summary>
    /// <returns>ANSI lines.</returns>
    public static IReadOnlyList<string> RenderCredits() =>
    [
        Ui.P("|13  CREDITS|07"),
        string.Empty,
        Ui.P("   |07Font: IBM VGA 8x16 from |15")
            + Ansi.LinkStart(new Uri("https://int10h.org/oldschool-pc-fonts/")) + Ui.P("The Ultimate Oldschool PC Font Pack") + Ansi.LinkEnd()
            + Ui.P("|07 by VileR,"),
        Ui.P("   |07licensed ")
            + Ansi.LinkStart(new Uri("https://creativecommons.org/licenses/by-sa/4.0/")) + Ui.P("|11CC BY-SA 4.0") + Ansi.LinkEnd()
            + Ui.P("|07."),
    ];

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        await Term.TransitionAsync(Transition.Wipe, Ui.Cls + Ui.Header("User list & hall of fame"), cancellationToken);
        var fame = await Context.Content.GetHallOfFameAsync(cancellationToken);
        var callers = await Context.Api.GetRecentCallersAsync(cancellationToken);
        var lines = RenderFame(fame)
            .Concat([string.Empty]).Concat(RenderCallers(callers.Value ?? []))
            .Concat([string.Empty]).Concat(RenderCredits())
            .ToList();
        await Ui.PageAsync(Term, lines, 18, cancellationToken);
        if (!callers.IsOk)
        {
            await Term.WriteAsync(Ui.Failure(callers), cancellationToken);
        }

        await Ui.PressAnyKeyAsync(Term, cancellationToken);
        return ScreenId.MainMenu;
    }
}

/// <summary>
/// [P] Page SysOp: leaves a short message and buzzes the SysOp's phone. Signed-in callers only.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class PageSysopScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.PageSysop;

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await Term.TransitionAsync(Transition.Wipe, Ui.Cls + Ui.Header("Page the SysOp"), cancellationToken);
        if (!session.IsMember)
        {
            await Term.WriteAsync(Ui.AccessDenied(session), cancellationToken);
            await Ui.PressAnyKeyAsync(Term, cancellationToken);
            return ScreenId.MainMenu;
        }

        await SayAsync(
            $"|07  The SysOp may be asleep, at work, or elbow-deep in a modem. Keep it short (|15{Limits.PageLength}|07 characters).\r\n" +
            "|08  ESC cancels.|07\r\n\r\n|11  Message: |07",
            cancellationToken);
        var text = await LineEditor.ReadLineAsync(Term, Math.Min(Limits.PageLength, 62), string.Empty, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            await SayAsync("\r\n|08  Page cancelled. The SysOp sleeps on.|07\r\n", cancellationToken);
            await Ui.PressAnyKeyAsync(Term, cancellationToken);
            return ScreenId.MainMenu;
        }

        await SayAsync("\r\n\r\n|07  Paging SysOp", cancellationToken);
        for (var i = 0; i < 3; i++)
        {
            await Term.BeepAsync();
            await SayAsync("|14 . ", cancellationToken);
            await Term.DelayAsync(TimeSpan.FromMilliseconds(350), cancellationToken);
        }

        var result = await Context.Api.PageSysopAsync(new PageSysopRequest(text), cancellationToken);
        await Term.WriteAsync(
            result.IsOk ? Ui.P("\r\n\r\n|10  SysOp has been alerted.|08 If they answer, it will be in a bulletin, a reply, or a dream.|07\r\n") : Ui.Failure(result),
            cancellationToken);
        await Ui.PressAnyKeyAsync(Term, cancellationToken);
        return ScreenId.MainMenu;
    }
}

/// <summary>
/// [S] Terminal settings: baud rate, sound and the RIP welcome card.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class SettingsScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.Settings;

    /// <summary>
    /// Builds the settings screen.
    /// </summary>
    /// <param name="settings">The current settings.</param>
    /// <returns>ANSI text.</returns>
    public static string Render(TerminalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        static string OnOff(bool on) => on ? "|10ON |07" : "|09OFF|07";
        return Ui.Cls + Ui.Header("Terminal settings") +
            "   " + Ui.Item("B", Ui.P($"Baud rate ......... |11{settings.BaudLabel}|07")) + "\r\n" +
            "   " + Ui.Item("A", Ui.P($"Modem sounds ...... {OnOff(settings.Sound)}")) + "\r\n" +
            "   " + Ui.Item("R", Ui.P($"RIP welcome card .. {OnOff(settings.RipCard)}")) + "\r\n\r\n" +
            Ui.P("|08   Settings are saved in this browser only. INSTANT also turns off most animation.|07\r\n\r\n") +
            "   " + Ui.Item("Q", "Back to the main menu") + Ui.P("\r\n\r\n|11   Change which? |07");
    }

    /// <summary>
    /// Applies one settings key.
    /// </summary>
    /// <param name="settings">The current settings.</param>
    /// <param name="key">The key pressed.</param>
    /// <returns>The new settings, or <see langword="null"/> when the key changes nothing.</returns>
    public static TerminalSettings? Apply(TerminalSettings settings, string key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (DoorKeys.Is(key, 'B'))
        {
            return settings.NextBaud();
        }

        if (DoorKeys.Is(key, 'A'))
        {
            return settings with { Sound = !settings.Sound };
        }

        return DoorKeys.Is(key, 'R') ? settings with { RipCard = !settings.RipCard } : null;
    }

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await Term.TransitionAsync(Transition.Wipe, Render(session.Settings), cancellationToken);
        while (true)
        {
            var key = await Term.ReadKeyAsync(cancellationToken);
            if (DoorKeys.Is(key, 'Q') || key == DoorKeys.Escape)
            {
                return ScreenId.MainMenu;
            }

            if (Apply(session.Settings, key) is { } next)
            {
                session.Settings = next;
                await next.SaveAsync(Context.Storage);
                await Term.ApplySettingsAsync(next);
                await Term.WriteAsync(Render(next), cancellationToken);
            }
        }
    }
}

/// <summary>
/// [G] Goodbye: time online, a farewell, then NO CARRIER and back to the Connect screen.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class GoodbyeScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.Goodbye;

    /// <summary>
    /// Builds the farewell text.
    /// </summary>
    /// <param name="handle">The caller's handle.</param>
    /// <param name="online">How long the caller was online.</param>
    /// <returns>ANSI text.</returns>
    public static string Render(string handle, TimeSpan online)
    {
        var seconds = Math.Max(0, (long)online.TotalSeconds);
        return Ui.Cls + Ui.P(string.Create(
            CultureInfo.InvariantCulture,
            $"\r\n\r\n|#201{TextWrap.Center("THANKS FOR CALLING PIZON BBS", 80)}\r\n\r\n" +
            $"|07{TextWrap.Center($"{PipeCodes.Escape(handle)}, you were online for {seconds:N0} seconds.", 80)}\r\n" +
            $"|08{TextWrap.Center("Your call has been logged on paper and filed in a shoebox.", 80)}\r\n\r\n" +
            $"|07{TextWrap.Center("Call again soon. The line is always open (unless someone is on it).", 80)}|07\r\n\r\n"));
    }

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var online = Context.Time.GetUtcNow() - session.ConnectedAt;
        await Term.TransitionAsync(Transition.ScrollOff, Ui.Cls, cancellationToken);
        await Term.WriteAsync(Render(session.Handle, online), cancellationToken);
        await Context.Presence.DisconnectAsync();
        await Term.DelayAsync(TimeSpan.FromSeconds(2), cancellationToken);
        await SayAsync("|07+++\r\n", cancellationToken);
        await Term.DelayAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
        await SayAsync("|07ATH0\r\n\r\n|15NO CARRIER|07\r\n", cancellationToken);
        await Term.DelayAsync(TimeSpan.FromSeconds(2), cancellationToken);
        Term.DiscardPendingKeys();
        session.ActingAsGuest = false;
        return ScreenId.Connect;
    }
}

/// <summary>
/// [L] Log out of Discord: clears the sign-in cookie and reloads the page.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class LogoutScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.Logout;

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        await SayAsync("\r\n|08Shredding your sign-in cookie...|07", cancellationToken);
        var result = await Context.Api.LogoutAsync(cancellationToken);
        if (!result.IsOk)
        {
            await Term.WriteAsync(Ui.Failure(result), cancellationToken);
            await Ui.PressAnyKeyAsync(Term, cancellationToken);
            return ScreenId.MainMenu;
        }

        await Context.Presence.DisconnectAsync();
        Context.Navigator.NavigateAway("/");
        return ScreenId.Exit;
    }
}

/// <summary>
/// [R] The RIPscrip welcome card: a 640x350 vector logo with a clickable button bar.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class RipCardScreen(ScreenContext context) : ScreenBase(context)
{
    /// <summary>The welcome card URL.</summary>
    public const string CardUrl = "content/welcome.rip";

    /// <inheritdoc />
    public override ScreenId Id => ScreenId.RipCard;

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var key = await Term.ShowRipAsync(CardUrl, cancellationToken);
        return key is null ? ScreenId.MainMenu : MainMenuScreen.Route(key, session) ?? ScreenId.MainMenu;
    }
}
