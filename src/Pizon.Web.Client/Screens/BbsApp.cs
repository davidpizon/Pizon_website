using System.Diagnostics.CodeAnalysis;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Screens.Hosting;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// The BBS itself: picks the first screen from the page address, then runs screens one after another until
/// the page leaves. It keeps the browser address in step with the screen (see <see cref="RouteTable"/>) and
/// follows Back and Forward by stopping the current screen and opening the one the address names. Screens
/// that have become pages (<c>Screens/Pages</c>) run through <see cref="PageScreen"/>, which hands the terminal
/// to <see cref="BbsHost"/> while the router shows the page.
/// </summary>
public sealed class BbsApp
{
    /// <summary>The message shown when Discord sign-in failed or was cancelled.</summary>
    public const string LoginFailedMessage = "LOGIN FAILED - NO CARRIER ON THAT LINE";

    private readonly ScreenContext context;
    private readonly BbsHost host;
    private ScreenId current = ScreenId.Exit;
    private ScreenId? requested;
    private CancellationTokenSource? screenStop;

    /// <summary>
    /// Initializes a new instance of the <see cref="BbsApp"/> class.
    /// </summary>
    /// <param name="context">The shared screen context.</param>
    /// <param name="host">The page host.</param>
    public BbsApp(ScreenContext context, BbsHost host)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        this.context = context;
        this.host = host;
    }

    /// <summary>Gets the screens that are pages now, run through <see cref="PageScreen"/>.</summary>
    public static IReadOnlySet<ScreenId> PageScreens { get; } = new HashSet<ScreenId>
    {
        ScreenId.MainMenu, ScreenId.Bulletins, ScreenId.MessageBoard, ScreenId.FileAreas, ScreenId.Doors, ScreenId.UserList,
    };

    /// <summary>
    /// Works out where a call starts from the page address, and notes it on the session.
    /// <list type="bullet">
    /// <item><c>/</c> dials up.</item>
    /// <item><c>/login</c> goes straight to the login screen; <c>resume=1</c> means back from Discord,
    /// <c>error=failed</c> shows the sign-in failure and <c>returnUrl</c> is where to go afterwards.</item>
    /// <item>Any other routed address is a link straight to that screen (no dial-up).</item>
    /// <item>The old <c>/?resume=login</c> and <c>/?login=failed</c> addresses still work.</item>
    /// </list>
    /// </summary>
    /// <param name="uri">The page address.</param>
    /// <param name="session">The call.</param>
    /// <returns>The first screen.</returns>
    public static ScreenId StartScreen(Uri uri, BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(session);
        var path = RouteTable.Normalize(uri.AbsolutePath);
        var failed = string.Equals(Query(uri, "login"), "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Query(uri, "error"), "failed", StringComparison.OrdinalIgnoreCase);
        var resumed = failed || Query(uri, "resume") is "1" ||
            string.Equals(Query(uri, "resume"), "login", StringComparison.OrdinalIgnoreCase);

        if (path == "/" && !resumed)
        {
            return ScreenId.Connect;
        }

        if (path == "/" || path.Equals("/login", StringComparison.OrdinalIgnoreCase))
        {
            session.Resumed = resumed;
            session.LoginError = failed ? LoginFailedMessage : null;
            if (RouteTable.TryResolve(Query(uri, "returnUrl"), out var back))
            {
                session.ReturnTo = back.Path;
            }

            return ScreenId.Login;
        }

        if (RouteTable.TryResolve(path, out var route))
        {
            session.DeepLink = true;
            return route.Screen;
        }

        return ScreenId.Connect;
    }

    /// <summary>
    /// Creates the screen for an id.
    /// </summary>
    /// <param name="id">The screen id.</param>
    /// <returns>The screen, or <see langword="null"/> for <see cref="ScreenId.Exit"/>.</returns>
    public IScreen? Create(ScreenId id) => PageScreens.Contains(id) ? new PageScreen(context, host, id) : id switch
    {
        ScreenId.Connect => new ConnectScreen(context),
        ScreenId.Handshake => new HandshakeScreen(context),
        ScreenId.Welcome => new WelcomeScreen(context),
        ScreenId.Login => new LoginScreen(context),
        ScreenId.SignIn => new SignInScreen(context),
        ScreenId.NewUserHandle => new HandleScreen(context, isNewUser: true),
        ScreenId.ChangeHandle => new HandleScreen(context, isNewUser: false),
        ScreenId.RipCard => new RipCardScreen(context),
        ScreenId.BoardNew => new BoardComposeScreen(context, reply: false),
        ScreenId.BoardReply => new BoardComposeScreen(context, reply: true),
        ScreenId.PageSysop => new PageSysopScreen(context),
        ScreenId.Settings => new SettingsScreen(context),
        ScreenId.Goodbye => new GoodbyeScreen(context),
        ScreenId.Logout => new LogoutScreen(context),
        ScreenId.SysopConsole => new SysopConsoleScreen(context),
        ScreenId.NotFound => new NotFoundScreen(context),
        _ => null,
    };

    /// <summary>
    /// Runs the call from <paramref name="start"/> until a screen returns <see cref="ScreenId.Exit"/> or the
    /// page goes away. A screen that fails drops the line back to the Connect screen.
    /// </summary>
    /// <param name="session">The call.</param>
    /// <param name="start">The first screen.</param>
    /// <param name="cancellationToken">Cancelled when the page goes away.</param>
    /// <returns>A task that completes when the call ends.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any screen failure must drop the caller back to the Connect screen instead of killing the app.")]
    public async Task RunAsync(BbsSession session, ScreenId start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Resumed)
        {
            session.ConnectedAt = context.Time.GetUtcNow();
            await context.Terminal.WriteAsync(PipeCodes.ToAnsi("|15CARRIER RESTORED - WELCOME BACK|07\r\n"), cancellationToken);
        }

        if (session.DeepLink)
        {
            start = await JoinAsync(session, start, cancellationToken);
        }

        context.Navigator.LocationChanged += OnLocationChanged;
        try
        {
            var next = RouteTable.Guard(start, session);
            var previous = next;
            while (!cancellationToken.IsCancellationRequested && Create(next) is { } screen)
            {
                current = next;
                SyncAddress(previous, current);
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                screenStop = stop;
                try
                {
                    next = await screen.RunAsync(session, stop.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception) when (requested is not null)
                {
                    // Back/Forward stopped the screen; the requested screen runs next.
                }
                catch (Exception)
                {
                    await context.Terminal.WriteAsync(PipeCodes.ToAnsi("\r\n\r\n|09LINE NOISE - CONNECTION LOST.|07\r\n\r\n|15NO CARRIER|07\r\n"), cancellationToken);
                    await context.Terminal.DelayAsync(TimeSpan.FromSeconds(2), cancellationToken);
                    next = ScreenId.Connect;
                }
                finally
                {
                    screenStop = null;
                }

                if (requested is { } asked)
                {
                    next = asked;
                    requested = null;
                }

                previous = current;
                next = RouteTable.Guard(next, session);
            }
        }
        finally
        {
            context.Navigator.LocationChanged -= OnLocationChanged;
            current = ScreenId.Exit;
        }
    }

    /// <summary>
    /// Handles an address change: Back, Forward, or a typed or clicked link to another screen. Changes this
    /// app made itself (the address already names the current screen) are ignored.
    /// </summary>
    /// <param name="sender">The navigator.</param>
    /// <param name="uri">The new address.</param>
    internal void OnLocationChanged(object? sender, Uri uri)
    {
        if (!RouteTable.TryResolve(uri.AbsolutePath, out var route) || route.Screen == current ||
            RouteTable.For(current)?.Path == route.Path)
        {
            return;
        }

        requested = route.Screen;
        screenStop?.Cancel();
    }

    private static string? Query(Uri uri, string key)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            var name = eq < 0 ? pair : pair[..eq];
            if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }

        return null;
    }

    /// <summary>
    /// Joins a call that started from a link: notes the time, asks the server who is calling and joins
    /// presence, so the linked screen draws straight away. A signed-in caller counts as logged on; one who
    /// signed in but never chose a handle picks one first and then comes back to the link.
    /// </summary>
    private async Task<ScreenId> JoinAsync(BbsSession session, ScreenId start, CancellationToken cancellationToken)
    {
        session.ConnectedAt = context.Time.GetUtcNow();
        session.Me = await context.Api.GetMeAsync(cancellationToken);
        if (session.Me.Authenticated && session.Me.Handle is null)
        {
            session.ReturnTo = RouteTable.For(start)?.Path;
            return ScreenId.NewUserHandle;
        }

        session.SignedOn = session.Me.Authenticated;
        await context.Presence.ConnectAsync(cancellationToken);
        return start;
    }

    /// <summary>
    /// Puts the screen's address in the address bar (screens without a route keep the current one, and an
    /// address under a nested route, such as <c>/bulletins/2</c>, already belongs to its screen).
    /// Moving on from an entrance screen replaces the history entry.
    /// </summary>
    private void SyncAddress(ScreenId from, ScreenId to)
    {
        if (RouteTable.For(to) is not { } route ||
            (RouteTable.TryResolve(context.Navigator.CurrentUri.AbsolutePath, out var here) && here.Screen == to))
        {
            return;
        }

        context.Navigator.Navigate(route.Path, replace: RouteTable.IsEntrance(from));
    }
}
