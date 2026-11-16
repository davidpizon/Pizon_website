using System.Diagnostics.CodeAnalysis;
using Pizon.Doors.Sdk.Text;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// The BBS itself: picks the first screen from the page address, then runs screens one after another until
/// the page leaves.
/// </summary>
public sealed class BbsApp
{
    /// <summary>The message shown when Discord sign-in failed or was cancelled.</summary>
    public const string LoginFailedMessage = "LOGIN FAILED - NO CARRIER ON THAT LINE";

    private readonly ScreenContext context;

    /// <summary>
    /// Initializes a new instance of the <see cref="BbsApp"/> class.
    /// </summary>
    /// <param name="context">The shared screen context.</param>
    public BbsApp(ScreenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
    }

    /// <summary>
    /// Works out where a call starts from the page address, and notes it on the session.
    /// <c>?resume=login</c> (back from Discord) and <c>?login=failed</c> skip the dial-up; <c>/sysop</c>
    /// asks for the SysOp console after sign-in.
    /// </summary>
    /// <param name="uri">The page address.</param>
    /// <param name="session">The call.</param>
    /// <returns>The first screen.</returns>
    public static ScreenId StartScreen(Uri uri, BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(session);
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (uri.AbsolutePath.TrimEnd('/').Equals("/sysop", StringComparison.OrdinalIgnoreCase))
        {
            session.SysopRequested = true;
        }

        if (query.Contains("login=failed", StringComparer.OrdinalIgnoreCase))
        {
            session.Resumed = true;
            session.LoginError = LoginFailedMessage;
            return ScreenId.Login;
        }

        if (query.Contains("resume=login", StringComparer.OrdinalIgnoreCase))
        {
            session.Resumed = true;
            return ScreenId.Login;
        }

        return ScreenId.Connect;
    }

    /// <summary>
    /// Creates the screen for an id.
    /// </summary>
    /// <param name="id">The screen id.</param>
    /// <returns>The screen, or <see langword="null"/> for <see cref="ScreenId.Exit"/>.</returns>
    public IScreen? Create(ScreenId id) => id switch
    {
        ScreenId.Connect => new ConnectScreen(context),
        ScreenId.Handshake => new HandshakeScreen(context),
        ScreenId.Welcome => new WelcomeScreen(context),
        ScreenId.Login => new LoginScreen(context),
        ScreenId.SignIn => new SignInScreen(context),
        ScreenId.NewUserHandle => new HandleScreen(context, isNewUser: true),
        ScreenId.ChangeHandle => new HandleScreen(context, isNewUser: false),
        ScreenId.RipCard => new RipCardScreen(context),
        ScreenId.MainMenu => new MainMenuScreen(context),
        ScreenId.Bulletins => new BulletinsScreen(context),
        ScreenId.MessageBoard => new MessageBoardScreen(context),
        ScreenId.FileAreas => new FileAreasScreen(context),
        ScreenId.Doors => new DoorsScreen(context),
        ScreenId.UserList => new UserListScreen(context),
        ScreenId.PageSysop => new PageSysopScreen(context),
        ScreenId.Settings => new SettingsScreen(context),
        ScreenId.Goodbye => new GoodbyeScreen(context),
        ScreenId.Logout => new LogoutScreen(context),
        ScreenId.SysopConsole => new SysopConsoleScreen(context),
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

        var current = start;
        while (!cancellationToken.IsCancellationRequested && Create(current) is { } screen)
        {
            try
            {
                current = await screen.RunAsync(session, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                await context.Terminal.WriteAsync(PipeCodes.ToAnsi("\r\n\r\n|09LINE NOISE - CONNECTION LOST.|07\r\n\r\n|15NO CARRIER|07\r\n"), cancellationToken);
                await context.Terminal.DelayAsync(TimeSpan.FromSeconds(2), cancellationToken);
                current = ScreenId.Connect;
            }
        }
    }
}
