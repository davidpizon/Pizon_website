using System.Globalization;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Rendering;
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
    /// Builds the door list: two lines per door (title and tags, then the description).
    /// </summary>
    /// <param name="doors">The enabled doors.</param>
    /// <param name="session">The call (for lock markers).</param>
    /// <returns>ANSI text.</returns>
    public static string Render(IReadOnlyList<DoorManifestEntry> doors, BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(session);
        var lines = new List<string>();
        if (doors.Count == 0)
        {
            lines.Add("|08All doors are closed for repairs. The SysOp is holding a screwdriver.|07");
        }

        for (var i = 0; i < doors.Count && i < 9; i++)
        {
            var d = doors[i];
            var locked = d.RequiresAuth && !session.IsMember;
            var tag = locked ? " |09[LOCKED - LVL 10]|07" : d.Scored ? " |13[SCORED]|07" : string.Empty;
            lines.Add(Ui.ItemPipe((i + 1).ToString(CultureInfo.InvariantCulture), $"|15{PipeCodes.Escape(d.Title)}") + tag);
            lines.Add($"|08    {PipeCodes.Escape(d.Description)}|07");
        }

        return new ScreenView(DoorsArt.Instance).SetLines(DoorsArt.List, lines).Render();
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
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> RenderFame(HallOfFame fame)
    {
        ArgumentNullException.ThrowIfNull(fame);
        var lines = new List<string> { "|13  HALL OF FAME|08 - legends of the board|07", string.Empty };
        foreach (var e in fame.Entries)
        {
            var handle = $"|15{PipeCodes.Escape(TextWrap.Fit(e.Handle, 16))}|07";
            if (e.Url is not null)
            {
                handle = "|{" + e.Url.AbsoluteUri + "}" + handle + "|]";
            }

            lines.Add($"   {handle} |11{PipeCodes.Escape(TextWrap.Fit(e.Clearance, 9))}|07 {PipeCodes.Escape(TextWrap.Fit(e.Note, 48))}");
        }

        return lines;
    }

    /// <summary>
    /// Builds the recent callers lines.
    /// </summary>
    /// <param name="callers">Recent callers.</param>
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> RenderCallers(IReadOnlyList<CallerDto> callers)
    {
        ArgumentNullException.ThrowIfNull(callers);
        var lines = new List<string> { "|13  LAST CALLERS|08 - signed-in callers only; guests leave no trace|07", string.Empty };
        if (callers.Count == 0)
        {
            lines.Add("|08   Nobody has signed the log yet.|07");
        }

        lines.AddRange(callers.Select(c => string.Create(
            CultureInfo.InvariantCulture,
            $"   |15{PipeCodes.Escape(TextWrap.Fit(c.Handle, 16))}|07 |11LVL {c.Level,3}|07  |08last call {Ui.Stamp(c.LastSeen)}|07")));
        return lines;
    }

    /// <summary>
    /// Builds the credits lines, including the attribution the CC BY-SA terminal font requires.
    /// </summary>
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> RenderCredits() =>
    [
        "|13  CREDITS|07",
        string.Empty,
        "   |07Font: IBM VGA 8x16 from |15|{https://int10h.org/oldschool-pc-fonts/}The Ultimate Oldschool PC Font Pack|]|07 by VileR,",
        "   |07licensed |{https://creativecommons.org/licenses/by-sa/4.0/}|11CC BY-SA 4.0|]|07.",
    ];

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        var view = new ScreenView(UsersArt.Instance);
        await Term.TransitionAsync(Transition.Wipe, view.Render(), cancellationToken);
        var fame = await Context.Content.GetHallOfFameAsync(cancellationToken);
        var callers = await Context.Api.GetRecentCallersAsync(cancellationToken);
        var callerLines = RenderCallers(callers.Value ?? []).Concat(callers.IsOk ? [] : [Ui.FailureText(callers)]);
        var lines = RenderFame(fame)
            .Concat([string.Empty]).Concat(callerLines)
            .Concat([string.Empty]).Concat(RenderCredits())
            .ToList();
        await view.PageAsync(Term, UsersArt.Body, UsersArt.More, lines, cancellationToken);
        await view.PressAnyKeyAsync(Term, cancellationToken);
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
        var view = new ScreenView(PageArt.Instance);
        if (!session.IsMember)
        {
            view.Set(PageArt.Status, Ui.AccessDeniedText(session));
            await Term.TransitionAsync(Transition.Wipe, view.Render(), cancellationToken);
            await view.PressAnyKeyAsync(Term, cancellationToken);
            return ScreenId.MainMenu;
        }

        await Term.TransitionAsync(Transition.Wipe, view.Render(), cancellationToken);
        await Term.WriteAsync(view.MoveTo(PageArt.Message), cancellationToken);
        var text = await LineEditor.ReadLineAsync(Term, Math.Min(Limits.PageLength, PageArt.Message.Width - 2), string.Empty, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            await view.UpdateAsync(Term, PageArt.Status, "|08Page cancelled. The SysOp sleeps on.|07", cancellationToken);
            await view.PressAnyKeyAsync(Term, cancellationToken);
            return ScreenId.MainMenu;
        }

        var paging = "|07Paging SysOp";
        for (var i = 0; i < 3; i++)
        {
            await Term.BeepAsync();
            paging += "|14 . ";
            await view.UpdateAsync(Term, PageArt.Status, paging, cancellationToken);
            await Term.DelayAsync(TimeSpan.FromMilliseconds(350), cancellationToken);
        }

        var result = await Context.Api.PageSysopAsync(new PageSysopRequest(text), cancellationToken);
        await view.UpdateAsync(
            Term,
            PageArt.Status,
            result.IsOk ? "|10SysOp has been alerted.|08 If they answer, it will be in a bulletin, a reply, or a dream.|07" : Ui.FailureText(result),
            cancellationToken);
        await view.PressAnyKeyAsync(Term, cancellationToken);
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
    /// Binds the settings art.
    /// </summary>
    /// <param name="settings">The current settings.</param>
    /// <returns>The bound view.</returns>
    public static ScreenView View(TerminalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        static string OnOff(bool on) => on ? "|10ON" : "|09OFF";
        return new ScreenView(SettingsArt.Instance)
            .Set(SettingsArt.Baud, settings.BaudLabel)
            .Set(SettingsArt.Sound, OnOff(settings.Sound))
            .Set(SettingsArt.Rip, OnOff(settings.RipCard));
    }

    /// <summary>
    /// Builds the settings screen.
    /// </summary>
    /// <param name="settings">The current settings.</param>
    /// <returns>ANSI text.</returns>
    public static string Render(TerminalSettings settings) => View(settings).Render();

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
                var view = View(next);
                await Term.WriteAsync(view.RenderField(SettingsArt.Baud) + view.RenderField(SettingsArt.Sound) + view.RenderField(SettingsArt.Rip), cancellationToken);
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
    /// Builds the farewell screen.
    /// </summary>
    /// <param name="handle">The caller's handle.</param>
    /// <param name="online">How long the caller was online.</param>
    /// <returns>ANSI text.</returns>
    public static string Render(string handle, TimeSpan online)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var seconds = Math.Max(0, (long)online.TotalSeconds);
        return new ScreenView(GoodbyeArt.Instance)
            .Set(GoodbyeArt.Farewell, string.Create(CultureInfo.InvariantCulture, $"{PipeCodes.Escape(handle)}, you were online for {seconds:N0} seconds."))
            .Render();
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
        session.SignedOn = false;
        session.ReturnTo = null;
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
/// "File not found": shown instead of a screen the caller may not open, without saying that it exists.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class NotFoundScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.NotFound;

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        var view = new ScreenView(NotFoundArt.Instance);
        await Term.WriteAsync(view.Render(), cancellationToken);
        await view.PressAnyKeyAsync(Term, cancellationToken);
        return ScreenId.MainMenu;
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
