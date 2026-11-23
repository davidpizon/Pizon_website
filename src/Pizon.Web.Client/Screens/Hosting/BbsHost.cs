using System.Diagnostics.CodeAnalysis;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Components;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Services;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens.Hosting;

/// <summary>
/// Runs the call. It owns the terminal once <see cref="Layout.BbsLayout"/> attaches it, works out where the
/// call starts from the address (dial-up, back from Discord, or a link straight to a page), and then follows
/// the address: for each route it applies <see cref="RouteTable.Guard"/> and runs the pump, which draws the
/// page for the current address, reads keys and hands them to it, and redraws when the page changes. Each
/// page's <see cref="BbsScreen"/> registers here when it renders, and never writes to the terminal while it
/// isn't the one on screen. Work without an address of its own (signing in, logging out) runs inside a
/// page's handler through <see cref="SignInAsync"/>, <see cref="LogOnAsync"/> and <see cref="LogOutAsync"/>.
/// </summary>
public sealed class BbsHost
{
    /// <summary>The message shown when Discord sign-in failed or was cancelled.</summary>
    public const string LoginFailedMessage = "LOGIN FAILED - NO CARRIER ON THAT LINE";

    /// <summary>Where Discord sends the caller back to.</summary>
    public const string ResumeUrl = "/login?resume=1";

    private readonly IExternalNavigator navigator;
    private readonly List<BbsScreen> screens = [];
    private readonly List<Func<Task>> refreshers = [];
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? reading;
    private CancellationTokenSource? routeStop;
    private string? running;
    private ScreenContext? context;
    private BbsScreen? drawn;
    private int drawnVersion;
    private int signals;
    private bool lineMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="BbsHost"/> class.
    /// </summary>
    /// <param name="navigator">The browser address.</param>
    public BbsHost(IExternalNavigator navigator)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        this.navigator = navigator;
        navigator.LocationChanged += OnLocationChanged;
    }

    /// <summary>Gets the call: one per browser tab.</summary>
    public BbsSession Session { get; } = new();

    /// <summary>Gets a task that completes once the terminal is attached.</summary>
    public Task Ready => ready.Task;

    /// <summary>Gets everything screens share. Only available once the terminal is attached.</summary>
    public ScreenContext Context => context ?? throw new InvalidOperationException("The terminal isn't attached yet.");

    /// <summary>Gets the terminal.</summary>
    public ITerminal Terminal => Context.Terminal;

    /// <summary>Gets the screen that is on the terminal now, if a page is showing.</summary>
    public BbsScreen? Live { get; private set; }

    /// <summary>Gets a token that is cancelled when the page on screen is left (Back, Forward or a link).</summary>
    public CancellationToken PageToken { get; private set; }

    /// <summary>Gets the current address's path, normalized (see <see cref="RouteTable.Normalize"/>).</summary>
    public string CurrentPath => RouteTable.Normalize(navigator.CurrentUri.AbsolutePath);

    /// <summary>Gets how many page screens are registered (for tests).</summary>
    internal int ScreenCount => screens.Count;

    /// <summary>
    /// Builds the sign-in URL. It comes back to <see cref="ResumeUrl"/>, carrying the address to open afterwards.
    /// </summary>
    /// <param name="dev">Use the Development-only fake sign-in instead of Discord.</param>
    /// <param name="returnTo">The local path to open after signing in, if any.</param>
    /// <returns>The URL to navigate to.</returns>
    public static string SignInUrl(bool dev, string? returnTo = null)
    {
        var resume = returnTo is null ? ResumeUrl : ResumeUrl + "&returnUrl=" + Uri.EscapeDataString(returnTo);
        return (dev ? "auth/dev?user=tester&returnUrl=" : "auth/discord?returnUrl=") + Uri.EscapeDataString(resume);
    }

    /// <summary>
    /// Attaches the terminal and the shared services, once per tab.
    /// </summary>
    /// <param name="screenContext">Everything screens share.</param>
    public void Attach(ScreenContext screenContext)
    {
        ArgumentNullException.ThrowIfNull(screenContext);
        context = screenContext;
        ready.TrySetResult();
    }

    /// <summary>
    /// Runs the call until the page goes away: starts it from the address, then runs the page for each
    /// address the caller goes to. A page that fails drops the line back to the Connect screen.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the page goes away.</param>
    /// <returns>A task that completes when the call ends.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any page failure must drop the caller back to the Connect screen instead of killing the app.")]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StartAsync(cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var path = CurrentPath;
                if (!RouteTable.TryResolve(path, out var route))
                {
                    await GoAsync("/", cancellationToken);
                    continue;
                }

                var decision = RouteTable.Guard(route, path, Session);
                if (decision == RouteDecision.LogOnFirst)
                {
                    await GoAsync("/login", cancellationToken);
                    continue;
                }

                using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                routeStop = stop;
                running = route.Path;
                try
                {
                    if (decision == RouteDecision.NotFound)
                    {
                        await NotFoundAsync(stop.Token);
                    }
                    else
                    {
                        await RunPagesAsync(route.Path, stop.Token);
                    }
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception) when (stop.IsCancellationRequested)
                {
                    // The address moved on (Back, Forward or a link); its page runs next.
                }
                catch (Exception)
                {
                    await DropLineAsync(cancellationToken);
                }
                finally
                {
                    routeStop = null;
                    running = null;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page went away.
        }
    }

    /// <summary>
    /// Goes to another address. The page on screen stops writing straight away.
    /// </summary>
    /// <param name="path">A local path.</param>
    /// <param name="replace">Whether to replace the history entry (moving on from the way in, so Back doesn't redial).</param>
    public void Navigate(string path, bool replace = false)
    {
        Live = null;
        navigator.Navigate(path, replace);
    }

    /// <summary>
    /// Finishes logging on: joins presence, then opens the address the caller was linked to
    /// (<see cref="BbsSession.ReturnTo"/>), or else the RIP card or the main menu. Runs inside the login or
    /// handle page; the way in is replaced in the history.
    /// </summary>
    /// <returns>A task that completes once the next address is open.</returns>
    public async Task LogOnAsync()
    {
        Session.SignedOn = true;
        await Context.Presence.ConnectAsync(PageToken);
        await Terminal.DelayAsync(TimeSpan.FromMilliseconds(600), PageToken);
        var returnTo = Session.ReturnTo;
        Session.ReturnTo = null;
        var linked = RouteTable.TryResolve(returnTo, out var route) && route.Path is not ("/" or "/login") && returnTo != "/handle/new";
        Navigate(linked ? returnTo! : Session.Settings.RipCard ? "/rip" : "/menu", replace: true);
    }

    /// <summary>
    /// Leaves the page for Discord sign-in (or the Development-only fake sign-in), which comes back to the
    /// login screen and then to <see cref="BbsSession.ReturnTo"/>.
    /// </summary>
    /// <param name="dev">Use the fake sign-in.</param>
    /// <returns>A task that completes once the browser is on its way.</returns>
    public async Task SignInAsync(bool dev = false)
    {
        var text = dev ? "\r\n|08Dialing the dev line...|07" : "\r\n|08Transferring you to the Discord switchboard... hold the line.|07";
        await Terminal.WriteAsync(PipeCodes.ToAnsi(text), PageToken);
        navigator.NavigateAway(SignInUrl(dev, Session.ReturnTo));
    }

    /// <summary>
    /// Logs out of Discord: clears the sign-in cookie and reloads the BBS. If the server refuses, says why
    /// and redraws the page on screen.
    /// </summary>
    /// <returns>A task that completes when done.</returns>
    public async Task LogOutAsync()
    {
        await Terminal.WriteAsync(PipeCodes.ToAnsi("\r\n|08Shredding your sign-in cookie...|07"), PageToken);
        var result = await Context.Api.LogoutAsync(PageToken);
        if (!result.IsOk)
        {
            await Terminal.WriteAsync(Ui.Failure(result), PageToken);
            await Ui.PressAnyKeyAsync(Terminal, PageToken);
            Live?.Redraw();
            return;
        }

        await Context.Presence.DisconnectAsync();
        navigator.NavigateAway("/");
    }

    /// <summary>
    /// Writes ANSI to the terminal, if one is attached. Used for live updates of the page on screen.
    /// </summary>
    /// <param name="ansi">Finished ANSI.</param>
    public void Write(string ansi)
    {
        if (context is not null)
        {
            _ = context.Terminal.WriteAsync(ansi, CancellationToken.None);
        }
    }

    /// <summary>
    /// Switches the terminal's line mode (the on-screen keyboard on phones) on while an input has the cursor.
    /// </summary>
    /// <param name="on">Whether an input is taking typing.</param>
    public void SetLineMode(bool on)
    {
        if (context is null || lineMode == on)
        {
            return;
        }

        lineMode = on;
        _ = context.Terminal.SetLineModeAsync(on);
    }

    /// <summary>
    /// Runs the pages of one route until the address leaves it: draws the newest page whose address belongs
    /// to <paramref name="route"/>, sends it keys, and redraws whenever another page of the same route
    /// replaces it (<c>/bulletins</c> to <c>/bulletins/2</c>) or the page asks to be redrawn.
    /// </summary>
    /// <param name="route">The route's path.</param>
    /// <param name="cancellationToken">Cancelled when the page goes away.</param>
    /// <returns>A task that completes when the address has moved to another route.</returns>
    internal async Task RunPagesAsync(string route, CancellationToken cancellationToken)
    {
        drawn = null;
        PageToken = cancellationToken;
        try
        {
            while (await NextAsync(route, cancellationToken) is { } page)
            {
                foreach (var refresh in refreshers.ToList())
                {
                    await refresh();
                }

                drawn = page;
                drawnVersion = page.Version;
                SetLineMode(false);
                await Terminal.TransitionAsync(page.Enter, page.Compose(), cancellationToken);
                Live = page;
                await page.AfterDrawAsync(cancellationToken);
                await PumpKeysAsync(route, page, cancellationToken);
            }
        }
        finally
        {
            SetLineMode(false);
            Live = null;
            drawn = null;
            PageToken = CancellationToken.None;
        }
    }

    /// <summary>Registers a page's screen once it has rendered.</summary>
    /// <param name="screen">The screen.</param>
    internal void Add(BbsScreen screen)
    {
        screen.Route = RouteOf(CurrentPath);
        screens.Add(screen);
        Signal();
    }

    /// <summary>Removes a page's screen when it goes away.</summary>
    /// <param name="screen">The screen.</param>
    internal void Remove(BbsScreen screen)
    {
        screens.Remove(screen);
        if (Live == screen)
        {
            Live = null;
        }

        Signal();
    }

    /// <summary>Registers a page's refresh, run before each draw so the page shows the call as it is now.</summary>
    /// <param name="refresh">Re-renders the page.</param>
    internal void AddRefresher(Func<Task> refresh) => refreshers.Add(refresh);

    /// <summary>Removes a page's refresh.</summary>
    /// <param name="refresh">The refresh passed to <see cref="AddRefresher"/>.</param>
    internal void RemoveRefresher(Func<Task> refresh) => refreshers.Remove(refresh);

    /// <summary>Wakes the pump: a page came or went, asked to be redrawn, has work queued, or the address moved.</summary>
    internal void Signal()
    {
        signals++;
        var old = changed;
        changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        old.TrySetResult();
        reading?.Cancel();
    }

    private static string? RouteOf(string path) => RouteTable.TryResolve(path, out var route) ? route.Path : null;

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
    /// Follows the address: stops the route's page when the address moves to another route (Back, Forward
    /// or a link), so long work such as the RIP card ends, and wakes the pump.
    /// </summary>
    private void OnLocationChanged(object? sender, Uri uri)
    {
        if (running is not null && RouteOf(uri.AbsolutePath) != running)
        {
            routeStop?.Cancel();
        }

        Signal();
    }

    /// <summary>
    /// Works out where the call starts and notes it on the session.
    /// <list type="bullet">
    /// <item><c>/</c> dials up (the Connect page).</item>
    /// <item><c>/login</c> is the login screen; <c>resume=1</c> means back from Discord, <c>error=failed</c>
    /// shows the sign-in failure and <c>returnUrl</c> is where to go afterwards. The old
    /// <c>/?resume=login</c> and <c>/?login=failed</c> addresses still work.</item>
    /// <item>Any other address is a link straight to that page (no dial-up): the host asks the server who
    /// is calling and joins presence, so the page draws straight away. A signed-in caller counts as logged
    /// on; one who signed in but never chose a handle picks one first and then comes back to the link.</item>
    /// </list>
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the page goes away.</param>
    /// <returns>A task that completes once the call is set up.</returns>
    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        var uri = navigator.CurrentUri;
        var path = CurrentPath;
        var failed = string.Equals(Query(uri, "login"), "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Query(uri, "error"), "failed", StringComparison.OrdinalIgnoreCase);
        var resumed = failed || Query(uri, "resume") is "1" ||
            string.Equals(Query(uri, "resume"), "login", StringComparison.OrdinalIgnoreCase);

        if (path == "/" && !resumed)
        {
            return;
        }

        if (path is "/" or "/login")
        {
            Session.Resumed = resumed;
            Session.LoginError = failed ? LoginFailedMessage : null;
            var back = Query(uri, "returnUrl");
            if (RouteTable.TryResolve(back, out _))
            {
                var end = back!.IndexOfAny(['?', '#']);
                Session.ReturnTo = RouteTable.Normalize(end < 0 ? back : back[..end]);
            }

            if (resumed)
            {
                Session.ConnectedAt = Context.Time.GetUtcNow();
                await Terminal.WriteAsync(PipeCodes.ToAnsi("|15CARRIER RESTORED - WELCOME BACK|07\r\n"), cancellationToken);
            }

            if (path == "/")
            {
                await GoAsync("/login", cancellationToken);
            }

            return;
        }

        if (!RouteTable.TryResolve(path, out _))
        {
            return;
        }

        Session.DeepLink = true;
        Session.ConnectedAt = Context.Time.GetUtcNow();
        Session.Me = await Context.Api.GetMeAsync(cancellationToken);
        if (Session.Me.Authenticated && Session.Me.Handle is null)
        {
            Session.ReturnTo = path;
            await GoAsync("/handle/new", cancellationToken);
            return;
        }

        Session.SignedOn = Session.Me.Authenticated;
        await Context.Presence.ConnectAsync(cancellationToken);
    }

    /// <summary>Moves to another address in place of this one and waits until the address has moved.</summary>
    private async Task GoAsync(string path, CancellationToken cancellationToken)
    {
        var moved = changed.Task;
        Navigate(path, replace: true);
        await moved.WaitAsync(cancellationToken);
    }

    /// <summary>"File not found": shown instead of a page the caller may not open, without saying that it exists.</summary>
    private async Task NotFoundAsync(CancellationToken cancellationToken)
    {
        var view = new ScreenView(NotFoundArt.Instance);
        await Terminal.WriteAsync(view.Render(), cancellationToken);
        await view.PressAnyKeyAsync(Terminal, cancellationToken);
        Navigate("/menu", replace: true);
    }

    /// <summary>A page failed: says so, then goes back to the Connect screen.</summary>
    private async Task DropLineAsync(CancellationToken cancellationToken)
    {
        await Terminal.WriteAsync(PipeCodes.ToAnsi("\r\n\r\n|09LINE NOISE - CONNECTION LOST.|07\r\n\r\n|15NO CARRIER|07\r\n"), cancellationToken);
        await Terminal.DelayAsync(TimeSpan.FromSeconds(2), cancellationToken);
        if (CurrentPath != "/")
        {
            await GoAsync("/", cancellationToken);
        }
    }

    private BbsScreen? Newest(string route) => screens.LastOrDefault(s => s.Route == route);

    private bool IsCurrent(string route, BbsScreen page) =>
        RouteOf(CurrentPath) == route && Newest(route) == page && drawn == page && page.Version == drawnVersion;

    private async Task<BbsScreen?> NextAsync(string route, CancellationToken cancellationToken)
    {
        while (true)
        {
            var wake = changed.Task;
            if (RouteOf(CurrentPath) != route)
            {
                return null;
            }

            if (Newest(route) is { } page)
            {
                return page;
            }

            await wake.WaitAsync(cancellationToken);
        }
    }

    private async Task PumpKeysAsync(string route, BbsScreen page, CancellationToken cancellationToken)
    {
        while (true)
        {
            var seen = signals;
            if (!IsCurrent(route, page))
            {
                return;
            }

            if (await page.RunQueuedAsync(cancellationToken))
            {
                continue;
            }

            string key;
            using (var reader = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                reading = reader;
                try
                {
                    if (signals != seen)
                    {
                        continue;
                    }

                    key = await Terminal.ReadKeyAsync(reader.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    continue;
                }
                finally
                {
                    reading = null;
                }
            }

            await page.HandleKeyAsync(key, cancellationToken);
        }
    }
}
