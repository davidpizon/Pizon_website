using System.Globalization;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

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
