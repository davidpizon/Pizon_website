using Pizon.Web.Client.Screens.Components;
using Pizon.Web.Client.Services;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens.Hosting;

/// <summary>
/// Hosts screen pages in the terminal. Each page's <see cref="BbsScreen"/> registers here when it renders;
/// while the BBS is on a page screen (see <see cref="PageScreen"/>), the host's pump draws the page for the
/// current address, reads keys and hands them to it, and redraws when the page changes. A page never
/// writes to the terminal while it isn't the one on screen.
/// </summary>
/// <remarks>
/// Phase 4 of <c>docs/plans/screen-pages.md</c>: the <see cref="BbsApp"/> loop still runs the legacy
/// screens and hands over to this pump for every screen that has become a page.
/// </remarks>
public sealed class BbsHost
{
    private readonly IExternalNavigator navigator;
    private readonly List<BbsScreen> screens = [];
    private readonly List<Func<Task>> refreshers = [];
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? reading;
    private ScreenContext? context;
    private ScreenId? leaving;
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
    /// Goes to another address. The page on screen stops writing straight away.
    /// </summary>
    /// <param name="path">A local path.</param>
    public void Navigate(string path)
    {
        Live = null;
        navigator.Navigate(path, replace: false);
    }

    /// <summary>
    /// Leaves the pages for a screen that has no address of its own (signing in, logging out).
    /// </summary>
    /// <param name="screen">The screen to run next.</param>
    public void Leave(ScreenId screen)
    {
        Live = null;
        leaving = screen;
        Signal();
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
    /// Runs the pages for one screen until the caller leaves it: draws the newest page whose address belongs
    /// to <paramref name="screen"/>, sends it keys, and redraws whenever another page for the same screen
    /// replaces it (<c>/bulletins</c> to <c>/bulletins/2</c>) or the page asks to be redrawn.
    /// </summary>
    /// <param name="screen">The screen whose pages to run.</param>
    /// <param name="cancellationToken">Cancelled when the address moves to another screen.</param>
    /// <returns>The screen a page left for (see <see cref="Leave"/>).</returns>
    public async Task<ScreenId> RunPagesAsync(ScreenId screen, CancellationToken cancellationToken)
    {
        leaving = null;
        drawn = null;
        PageToken = cancellationToken;
        try
        {
            while (true)
            {
                var page = await NextAsync(screen, cancellationToken);
                if (page is null)
                {
                    return leaving!.Value;
                }

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
                await PumpKeysAsync(screen, page, cancellationToken);
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
        screen.Screen = RouteTable.TryResolve(CurrentPath, out var route) ? route.Screen : null;
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

    /// <summary>Wakes the pump: a page came or went, asked to be redrawn, or has work queued.</summary>
    internal void Signal()
    {
        signals++;
        var old = changed;
        changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        old.TrySetResult();
        reading?.Cancel();
    }

    private BbsScreen? Newest(ScreenId screen) => screens.LastOrDefault(s => s.Screen == screen);

    private bool IsCurrent(ScreenId screen, BbsScreen page) =>
        leaving is null && Newest(screen) == page && drawn == page && page.Version == drawnVersion;

    private async Task<BbsScreen?> NextAsync(ScreenId screen, CancellationToken cancellationToken)
    {
        while (true)
        {
            var wake = changed.Task;
            if (leaving is not null)
            {
                return null;
            }

            if (Newest(screen) is { } page)
            {
                return page;
            }

            await wake.WaitAsync(cancellationToken);
        }
    }

    private async Task PumpKeysAsync(ScreenId screen, BbsScreen page, CancellationToken cancellationToken)
    {
        while (true)
        {
            var seen = signals;
            if (!IsCurrent(screen, page))
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
