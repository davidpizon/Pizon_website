using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// Sign in with Discord, or connect as GUEST. Callers already signed in skip straight through.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class LoginScreen(ScreenContext context) : ScreenBase(context)
{
    /// <summary>Where Discord sends the caller back to.</summary>
    public const string ResumeUrl = "/?resume=login";

    /// <inheritdoc />
    public override ScreenId Id => ScreenId.Login;

    /// <summary>
    /// Builds the sign-in URL.
    /// </summary>
    /// <param name="dev">Use the Development-only fake sign-in instead of Discord.</param>
    /// <returns>The URL to navigate to.</returns>
    public static string SignInUrl(bool dev) =>
        (dev ? "auth/dev?user=tester&returnUrl=" : "auth/discord?returnUrl=") + Uri.EscapeDataString(ResumeUrl);

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Me = await Context.Api.GetMeAsync(cancellationToken);
        if (session.Me.Authenticated && !session.ActingAsGuest)
        {
            if (session.Me.Handle is null)
            {
                return ScreenId.NewUserHandle;
            }

            await SayAsync($"\r\n|10WELCOME BACK, |15{PipeCodes.Escape(session.Me.Handle)}|10.|07\r\n", cancellationToken);
            return await AfterLoginAsync(session, cancellationToken);
        }

        if (session.LoginError is not null)
        {
            await SayAsync($"\r\n|09{session.LoginError}|07\r\n", cancellationToken);
            session.LoginError = null;
        }

        await SayAsync("\r\n|11How do you wish to connect?\r\n\r\n", cancellationToken);
        var options = "  ";
        if (session.Me.DiscordEnabled)
        {
            options += Ui.Item("D", "Sign in with Discord") + "    ";
        }

        if (session.Me.DevLoginEnabled)
        {
            options += Ui.Item("V", Ui.P("|08Dev sign-in|07")) + "    ";
        }

        options += Ui.Item(DoorKeys.Enter, "Connect as GUEST", "Enter");
        await Term.WriteAsync(options + "\r\n", cancellationToken);

        while (true)
        {
            var key = await Term.ReadKeyAsync(cancellationToken);
            if (DoorKeys.Is(key, 'D') && session.Me.DiscordEnabled)
            {
                return ScreenId.SignIn;
            }

            if (DoorKeys.Is(key, 'V') && session.Me.DevLoginEnabled)
            {
                await SayAsync("\r\n|08Dialing the dev line...|07", cancellationToken);
                Context.Navigator.NavigateAway(SignInUrl(dev: true));
                return ScreenId.Exit;
            }

            if (key == DoorKeys.Enter || DoorKeys.Is(key, 'G'))
            {
                await SayAsync("\r\n|07Connecting as |14GUEST|07. Security level 0. Wipe your feet.\r\n", cancellationToken);
                return await AfterLoginAsync(session, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Finishes logging in: joins presence and picks the RIP card or the main menu.
    /// </summary>
    /// <param name="session">The call.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The next screen.</returns>
    public async Task<ScreenId> AfterLoginAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await Context.Presence.ConnectAsync(cancellationToken);
        await Term.DelayAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
        if (session.SysopRequested && session.IsSysop)
        {
            session.SysopRequested = false;
            return ScreenId.SysopConsole;
        }

        return session.Settings.RipCard ? ScreenId.RipCard : ScreenId.MainMenu;
    }
}

/// <summary>
/// Leaves the page for Discord sign-in.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class SignInScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.SignIn;

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        await SayAsync("\r\n|08Transferring you to the Discord switchboard... hold the line.|07", cancellationToken);
        Context.Navigator.NavigateAway(LoginScreen.SignInUrl(dev: false));
        return ScreenId.Exit;
    }
}

/// <summary>
/// "NEW USER - CHOOSE YOUR HANDLE" for first sign-ins, and [H] Change Handle afterwards.
/// </summary>
/// <param name="context">The shared screen context.</param>
/// <param name="isNewUser">Whether this is the first sign-in (otherwise a handle change).</param>
public sealed class HandleScreen(ScreenContext context, bool isNewUser) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => isNewUser ? ScreenId.NewUserHandle : ScreenId.ChangeHandle;

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!isNewUser && !session.IsMember)
        {
            await Term.WriteAsync(Ui.AccessDenied(session), cancellationToken);
            await Ui.PressAnyKeyAsync(Term, cancellationToken);
            return ScreenId.MainMenu;
        }

        await Term.TransitionAsync(Transition.Wipe, Ui.Cls + Ui.Header(isNewUser ? "New user - choose your handle" : "Change handle"), cancellationToken);
        await SayAsync(
            "|07Your handle is how the board knows you: |153-16|07 letters, digits, |15_|07 or |15-|07.\r\n" +
            "|08Edit the suggestion or type your own. ESC to cancel.|07\r\n\r\n",
            cancellationToken);
        var suggestion = isNewUser ? session.Me.SuggestedHandle ?? string.Empty : session.Me.Handle ?? string.Empty;
        while (true)
        {
            await SayAsync("|11Handle: |15", cancellationToken);
            var handle = await LineEditor.ReadLineAsync(Term, Limits.HandleMax, suggestion, cancellationToken);
            await SayAsync("|07\r\n", cancellationToken);
            if (handle is null)
            {
                if (isNewUser)
                {
                    session.ActingAsGuest = true;
                    await SayAsync("|08No handle, no problem: connecting as |14GUEST|08 for this call.|07\r\n", cancellationToken);
                    return await new LoginScreen(Context).AfterLoginAsync(session, cancellationToken);
                }

                return ScreenId.MainMenu;
            }

            if (!Limits.IsValidHandleShape(handle))
            {
                await SayAsync("|09That handle won't fit through the phone line. 3-16 letters, digits, _ or -.|07\r\n", cancellationToken);
                suggestion = handle;
                continue;
            }

            var result = await Context.Api.SetHandleAsync(handle, cancellationToken);
            if (result.IsOk)
            {
                session.Me = await Context.Api.GetMeAsync(cancellationToken);
                await SayAsync($"|10Handle registered: |15{PipeCodes.Escape(handle)}|10. Welcome to PIZON BBS.|07\r\n", cancellationToken);
                if (isNewUser)
                {
                    return await new LoginScreen(Context).AfterLoginAsync(session, cancellationToken);
                }

                await Ui.PressAnyKeyAsync(Term, cancellationToken);
                return ScreenId.MainMenu;
            }

            await Term.WriteAsync(Ui.Failure(result), cancellationToken);
            suggestion = handle;
        }
    }
}
