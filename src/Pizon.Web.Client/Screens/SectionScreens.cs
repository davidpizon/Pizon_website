using System.Globalization;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

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
        return key is null ? ScreenId.MainMenu : Pages.MainMenuPage.Route(key, session) ?? ScreenId.MainMenu;
    }
}
