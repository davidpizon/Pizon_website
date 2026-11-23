using System.Net;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Screens;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// Tests for writing on the message board (the legacy compose screen behind <c>/board/new</c> and <c>/board/reply</c>).
/// </summary>
public sealed class BoardComposeScreenTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Guests are refused and nothing is posted.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GuestsCannotPost()
    {
        var h = new Harness();
        h.Terminal.Keys("x");
        var screen = new BoardComposeScreen(h.Context, reply: false);
        Assert.Equal(ScreenId.MessageBoard, await screen.RunAsync(h.Session, CancellationToken.None));
        Assert.Equal(ScreenId.BoardNew, screen.Id);
        Assert.Contains("ACCESS DENIED", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Empty(h.Api.Calls);
    }

    /// <summary>A member posts, fails to reply to a missing message, replies, and abandons a message.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task MemberPostsAndReplies()
    {
        var h = new Harness().Member();
        h.Api.Messages = new(ApiStatus.Ok, [new MessageDto(1, null, "Trinity", "First!", T0)]);
        h.Terminal
            .Type("Hello").Keys(DoorKeys.Enter, "x")
            .Type("99").Keys("x")
            .Type("abc").Keys("x")
            .Type("1").Type("Reply").Keys(DoorKeys.Enter, "x")
            .Keys(DoorKeys.Escape, "x");
        var post = new BoardComposeScreen(h.Context, reply: false);
        var reply = new BoardComposeScreen(h.Context, reply: true);
        await post.RunAsync(h.Session, CancellationToken.None);
        await reply.RunAsync(h.Session, CancellationToken.None);
        await reply.RunAsync(h.Session, CancellationToken.None);
        await reply.RunAsync(h.Session, CancellationToken.None);
        await post.RunAsync(h.Session, CancellationToken.None);

        Assert.Equal(ScreenId.BoardReply, reply.Id);
        Assert.Equal(["post::Hello", "post:1:Reply"], h.Api.Calls);
        Assert.Contains("MESSAGE QUEUED FOR SYSOP REVIEW", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("No such message", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("Message abandoned", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>A rejected post shows why.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ShowsPostFailures()
    {
        var h = new Harness().Member();
        h.Api.WriteResult = new ApiResult(ApiStatus.RateLimited);
        h.Terminal.Type("Hi").Keys(DoorKeys.Enter, "x");
        await new BoardComposeScreen(h.Context, reply: false).RunAsync(h.Session, CancellationToken.None);
        Assert.Contains("SPEED RACER", h.Terminal.Plain, StringComparison.Ordinal);
    }
}

/// <summary>
/// Tests for the Page SysOp, settings, goodbye, logout and RIP card screens.
/// </summary>
public sealed class SectionScreenTests
{
    /// <summary>Guests can't page; members can cancel, page, or hit the rate limit.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PagesTheSysop()
    {
        var guest = new Harness();
        guest.Terminal.Keys("x");
        var screen = new PageSysopScreen(guest.Context);
        Assert.Equal(ScreenId.MainMenu, await screen.RunAsync(guest.Session, CancellationToken.None));
        Assert.Equal(ScreenId.PageSysop, screen.Id);
        Assert.Contains("ACCESS DENIED", guest.Terminal.Plain, StringComparison.Ordinal);

        var h = new Harness().Member();
        h.Terminal.Keys(DoorKeys.Escape, "x").Type("Hi there").Keys("x");
        await new PageSysopScreen(h.Context).RunAsync(h.Session, CancellationToken.None);
        await new PageSysopScreen(h.Context).RunAsync(h.Session, CancellationToken.None);
        Assert.Contains("Page cancelled", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("SysOp has been alerted", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(3, h.Terminal.Beeps);
        Assert.Equal(["page:Hi there"], h.Api.Calls);

        h.Api.WriteResult = new ApiResult(ApiStatus.RateLimited);
        h.Terminal.Type("again").Keys("x");
        await new PageSysopScreen(h.Context).RunAsync(h.Session, CancellationToken.None);
        Assert.Contains("SPEED RACER", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>Settings keys cycle baud and toggle sound and the RIP card.</summary>
    [Fact]
    public void AppliesSettingsKeys()
    {
        var s = new TerminalSettings();
        Assert.Equal(0, SettingsScreen.Apply(s, "b")!.Baud);
        Assert.True(SettingsScreen.Apply(s, "A")!.Sound);
        Assert.False(SettingsScreen.Apply(s, "r")!.RipCard);
        Assert.Null(SettingsScreen.Apply(s, "x"));
        Assert.Contains("9600 BAUD", PipeCodes.StripAnsi(SettingsScreen.Render(s)), StringComparison.Ordinal);
    }

    /// <summary>The settings screen saves and applies each change.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SettingsScreenSaves()
    {
        var h = new Harness();
        h.Terminal.Keys("B", "A", "R", "x", "Q");
        var screen = new SettingsScreen(h.Context);
        Assert.Equal(ScreenId.MainMenu, await screen.RunAsync(h.Session, CancellationToken.None));
        Assert.Equal(ScreenId.Settings, screen.Id);
        Assert.Equal(new TerminalSettings(0, true, false), h.Session.Settings);
        Assert.Equal(3, h.Terminal.Applied.Count);
        Assert.Equal(h.Session.Settings, await TerminalSettings.LoadAsync(h.Terminal));
        Assert.Contains("INSTANT", h.Terminal.Plain, StringComparison.Ordinal);

        h.Terminal.Keys(DoorKeys.Escape);
        Assert.Equal(ScreenId.MainMenu, await screen.RunAsync(h.Session, CancellationToken.None));
    }

    /// <summary>Goodbye reports the time online, hangs up and returns to Connect.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GoodbyeHangsUp()
    {
        var h = new Harness().Member();
        h.Session.ConnectedAt = h.Time.Now.AddSeconds(-1234);
        h.Session.ActingAsGuest = true;
        var screen = new GoodbyeScreen(h.Context);
        Assert.Equal(ScreenId.Connect, await screen.RunAsync(h.Session, CancellationToken.None));
        Assert.Equal(ScreenId.Goodbye, screen.Id);
        Assert.Contains("GUEST, you were online for 1,234 seconds", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("NO CARRIER", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(1, h.Presence.Disconnects);
        Assert.False(h.Session.ActingAsGuest);
        Assert.Equal([Transition.ScrollOff], h.Terminal.Transitions);
        Assert.Contains("0 seconds", PipeCodes.StripAnsi(GoodbyeScreen.Render("x", TimeSpan.FromSeconds(-5))), StringComparison.Ordinal);
    }

    /// <summary>Logout reloads the page; a failed logout stays on the board.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LogsOut()
    {
        var h = new Harness().Member();
        var screen = new LogoutScreen(h.Context);
        Assert.Equal(ScreenId.Exit, await screen.RunAsync(h.Session, CancellationToken.None));
        Assert.Equal(ScreenId.Logout, screen.Id);
        Assert.Equal(["/"], h.Navigator.Navigations);
        Assert.Equal(1, h.Presence.Disconnects);

        h.Api.WriteResult = new ApiResult(ApiStatus.Failed);
        h.Terminal.Keys("x");
        Assert.Equal(ScreenId.MainMenu, await screen.RunAsync(h.Session, CancellationToken.None));
        Assert.Contains("LINE NOISE", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>The RIP card routes its buttons through the main menu.</summary>
    /// <param name="key">The key the card returned.</param>
    /// <param name="expected">The expected screen.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(null, ScreenId.MainMenu)]
    [InlineData("1", ScreenId.Bulletins)]
    [InlineData("z", ScreenId.MainMenu)]
    public async Task RipCardRoutes(string? key, ScreenId expected)
    {
        var h = new Harness();
        h.Terminal.RipKey = key;
        var screen = new RipCardScreen(h.Context);
        Assert.Equal(ScreenId.RipCard, screen.Id);
        Assert.Equal(expected, await screen.RunAsync(h.Session, CancellationToken.None));
    }
}

/// <summary>
/// Tests for the SysOp console.
/// </summary>
public sealed class SysopConsoleScreenTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly PendingMessageDto[] Queue =
    [
        new(1, null, "Neo", 10, "approve me", T0),
        new(2, 1, "Spammer", 20, "buy now", T0),
        new(3, null, "Skip", 30, "later", T0),
    ];

    /// <summary>Everyone but the SysOp goes straight back to the menu.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task HiddenFromOthers()
    {
        var h = new Harness().Member();
        var screen = new SysopConsoleScreen(h.Context);
        Assert.Equal(ScreenId.SysopConsole, screen.Id);
        Assert.Equal(ScreenId.MainMenu, await screen.RunAsync(h.Session, CancellationToken.None));
        Assert.Empty(h.Terminal.Output);
    }

    /// <summary>The SysOp approves, bans, skips, reads pages and deletes a message.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ModeratesAndDeletes()
    {
        var h = new Harness().Member(sysop: true);
        h.Api.Queue = new(ApiStatus.Ok, Queue);
        h.Api.Pages = new(ApiStatus.Ok, [new SysopPageDto(5, "Neo", "hello", T0, false), new SysopPageDto(6, "Old", "seen", T0, true)]);
        h.Terminal
            .Keys("1", "z", "A", "B", "N")
            .Keys("2", "x")
            .Keys("3").Type("abc")
            .Keys("3").Type("7").Keys("N")
            .Keys("3").Type("7").Keys("Y", "x")
            .Keys("9", "Q");
        Assert.Equal(ScreenId.MainMenu, await new SysopConsoleScreen(h.Context).RunAsync(h.Session, CancellationToken.None));

        Assert.Equal(["moderate:1:True", "ban:20", "moderate:2:False", "read:5", "delete:7"], h.Api.Calls);
        Assert.Contains("[1/3]", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("reply to #1", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("hello", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("It never happened", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>Failures are shown, a failed ban doesn't reject, and Q leaves the queue.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ShowsFailures()
    {
        var h = new Harness().Member(sysop: true);
        h.Api.Queue = new(ApiStatus.Ok, Queue);
        h.Api.WriteResult = new ApiResult(ApiStatus.Forbidden);
        h.Terminal
            .Keys("1", "X", "x", "B", "x", "Q")
            .Keys("3").Type("7").Keys("y", "x")
            .Keys(DoorKeys.Escape);
        await new SysopConsoleScreen(h.Context).RunAsync(h.Session, CancellationToken.None);

        Assert.Equal(["moderate:1:False", "ban:20", "delete:7"], h.Api.Calls);
        Assert.Contains("THE SYSOP HAS SPOKEN", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>Empty and failed lists say so.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task EmptyAndFailedLists()
    {
        var h = new Harness().Member(sysop: true);
        h.Terminal.Keys("1", "x", "2", "x", "q");
        await new SysopConsoleScreen(h.Context).RunAsync(h.Session, CancellationToken.None);
        Assert.Contains("Nothing waiting", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("No pages", h.Terminal.Plain, StringComparison.Ordinal);

        var f = new Harness().Member(sysop: true);
        f.Api.Queue = new(ApiStatus.Failed);
        f.Api.Pages = new(ApiStatus.Failed);
        f.Terminal.Keys("1", "x", "2", "x", "q");
        await new SysopConsoleScreen(f.Context).RunAsync(f.Session, CancellationToken.None);
        Assert.Equal(2, f.Terminal.Plain.Split("LINE NOISE").Length - 1);
    }
}

/// <summary>
/// Tests for the BBS loop.
/// </summary>
public sealed class BbsAppTests
{
    /// <summary>
    /// The page address picks the first screen: <c>/</c> dials, <c>/login</c> carries resume, error and a
    /// checked <c>returnUrl</c>, the old addresses still work, and any other route is a link straight in.
    /// </summary>
    /// <param name="url">The page address.</param>
    /// <param name="expected">The first screen.</param>
    /// <param name="resumed">Whether the call resumes.</param>
    /// <param name="returnTo">The screen to open after logging on.</param>
    /// <param name="deepLink">Whether the call skips the dial-up.</param>
    [Theory]
    [InlineData("http://x/", ScreenId.Connect, false, null, false)]
    [InlineData("http://x/?resume=login", ScreenId.Login, true, null, false)]
    [InlineData("http://x/?a=1&LOGIN=FAILED", ScreenId.Login, true, null, false)]
    [InlineData("http://x/login", ScreenId.Login, false, null, false)]
    [InlineData("http://x/login?resume=1&returnUrl=%2FPage", ScreenId.Login, true, "/page", false)]
    [InlineData("http://x/login?error=failed&returnUrl=%2F%2Fevil.example", ScreenId.Login, true, null, false)]
    [InlineData("http://x/login?returnUrl", ScreenId.Login, false, null, false)]
    [InlineData("http://x/Bulletins/", ScreenId.Bulletins, false, null, true)]
    [InlineData("http://x/nowhere", ScreenId.Connect, false, null, false)]
    public void PicksTheStartScreen(string url, ScreenId expected, bool resumed, string? returnTo, bool deepLink)
    {
        var session = new BbsSession();
        Assert.Equal(expected, BbsApp.StartScreen(new Uri(url), session));
        Assert.Equal(resumed, session.Resumed);
        Assert.Equal(returnTo, session.ReturnTo);
        Assert.Equal(deepLink, session.DeepLink);
        Assert.Equal(url.Contains("failed", StringComparison.OrdinalIgnoreCase) ? BbsApp.LoginFailedMessage : null, session.LoginError);
    }

    /// <summary>A link straight to a public screen draws it with no dial-up, after asking who is calling.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task OpensALinkStraightAway()
    {
        var h = new Harness().Member();
        h.Navigator.CurrentUri = new Uri("http://localhost/settings");
        using var stop = new CancellationTokenSource();
        h.Terminal.StopWhenEmpty = stop;
        h.Session.Me = MeResponse.Guest;
        var start = BbsApp.StartScreen(h.Navigator.CurrentUri, h.Session);
        await h.App().RunAsync(h.Session, start, stop.Token);

        Assert.Contains("TERMINAL SETTINGS", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Modem ready", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(h.Time.Now, h.Session.ConnectedAt);
        Assert.True(h.Session.SignedOn);
        Assert.Equal("Neo", h.Session.Me.Handle);
        Assert.Equal(1, h.Presence.Connects);
        Assert.Empty(h.Navigator.Moves);
    }

    /// <summary>
    /// A guest's link to a member screen goes to login first (replacing the address), then back to the
    /// screen, which shows ACCESS DENIED to a caller who stays a guest.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SendsGuestsToLoginFirst()
    {
        var h = new Harness();
        h.Session.Settings = h.Session.Settings with { RipCard = false };
        h.Navigator.CurrentUri = new Uri("http://localhost/page");
        using var stop = new CancellationTokenSource();
        h.Terminal.StopWhenEmpty = stop;
        h.Navigator.OnNavigate = path => StopAtMenu(path, stop);
        h.Terminal.Keys(DoorKeys.Enter, "x");
        var start = BbsApp.StartScreen(h.Navigator.CurrentUri, h.Session);
        await h.App().RunAsync(h.Session, start, stop.Token);

        Assert.Equal([("/login", true), ("/page", true), ("/menu", false)], h.Navigator.Moves);
        Assert.Contains("ACCESS DENIED", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>A signed-in member who isn't the SysOp gets "file not found" for the SysOp console.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task HidesTheConsoleFromMembers()
    {
        var h = new Harness().Member();
        h.Navigator.CurrentUri = new Uri("http://localhost/sysop");
        using var stop = new CancellationTokenSource();
        h.Terminal.StopWhenEmpty = stop;
        h.Navigator.OnNavigate = path => StopAtMenu(path, stop);
        h.Terminal.Keys("x");
        var start = BbsApp.StartScreen(h.Navigator.CurrentUri, h.Session);
        await h.App().RunAsync(h.Session, start, stop.Token);

        Assert.Contains("FILE NOT FOUND ON THIS DRIVE", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("SYSOP CONSOLE", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal([("/menu", false)], h.Navigator.Moves);
    }

    /// <summary>A signed-in caller without a handle picks one before the linked screen opens.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NewUsersPickAHandleBeforeTheLink()
    {
        var h = new Harness();
        h.Api.Me = new MeResponse(true, null, "Neo", 10, false, true, false);
        h.Navigator.CurrentUri = new Uri("http://localhost/settings");
        using var stop = new CancellationTokenSource();
        h.Terminal.StopWhenEmpty = stop;
        h.Terminal.Keys(DoorKeys.Escape);
        var start = BbsApp.StartScreen(h.Navigator.CurrentUri, h.Session);
        await h.App().RunAsync(h.Session, start, stop.Token);

        Assert.Contains("NEW USER - CHOOSE YOUR HANDLE", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("TERMINAL SETTINGS", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.True(h.Session.ActingAsGuest);
    }

    /// <summary>
    /// Back/Forward to another screen stops the current one and opens it; unknown addresses and the current
    /// screen's own address are ignored.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task FollowsBackAndForward()
    {
        var h = new Harness();
        h.Navigator.CurrentUri = new Uri("http://localhost/settings");
        using var stop = new CancellationTokenSource();
        h.Terminal.StopWhenEmpty = stop;
        h.Navigator.OnNavigate = path => StopAtMenu(path, stop);
        h.Terminal.Keys("x", "y");
        h.Terminal.OnRead = key =>
        {
            if (key == "x")
            {
                h.Navigator.Go("/nowhere");
                h.Navigator.Go("/settings");
                h.Navigator.Go("/rip");
            }
        };
        await h.App().RunAsync(h.Session, ScreenId.Settings, stop.Token);

        Assert.Equal([("/menu", false)], h.Navigator.Moves);
        Assert.Contains("TERMINAL SETTINGS", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>Every screen id but Exit has a screen.</summary>
    [Fact]
    public void CreatesEveryScreen()
    {
        var app = new Harness().App();
        foreach (var id in Enum.GetValues<ScreenId>())
        {
            var screen = app.Create(id);
            if (id == ScreenId.Exit)
            {
                Assert.Null(screen);
            }
            else
            {
                Assert.Equal(id, screen!.Id);
            }
        }
    }

    /// <summary>A resumed call says CARRIER RESTORED and runs until a screen exits.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RunsUntilExit()
    {
        var h = new Harness();
        h.Api.Me = MeResponse.Guest with { DevLoginEnabled = true };
        h.Session.Resumed = true;
        h.Terminal.Keys("v");
        await h.App().RunAsync(h.Session, ScreenId.Login, CancellationToken.None);
        Assert.Contains("CARRIER RESTORED", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(h.Time.Now, h.Session.ConnectedAt);
        Assert.Single(h.Navigator.Navigations);
    }

    /// <summary>A crashing screen drops to Connect; cancellation ends the call.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RecoversFromCrashes()
    {
        var h = new Harness();
        using var stop = new CancellationTokenSource();
        h.Terminal.StopWhenEmpty = stop;
        h.Terminal.Keys(FakeTerminal.Crash);
        await h.App().RunAsync(h.Session, ScreenId.Settings, stop.Token);
        Assert.Contains("LINE NOISE - CONNECTION LOST", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("Modem ready", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.True(stop.IsCancellationRequested);
    }

    /// <summary>Ends a test's call once the BBS moves to the main menu, which is a page this test doesn't render.</summary>
    private static void StopAtMenu(string path, CancellationTokenSource stop)
    {
        if (path == "/menu")
        {
            stop.Cancel();
        }
    }
}

/// <summary>
/// Makes sure the sample content in <see cref="Harness"/> is what the screens see.
/// </summary>
public sealed class HarnessTests
{
    /// <summary>A missing response is a 404 for the fake handler.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task UnknownPathsAre404()
    {
        using var client = new FakeHttp().Client();
        using var response = await client.GetAsync(new Uri("nope", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.IsType<BulletinBoard>(await new Harness().Content.GetBulletinsAsync());
    }
}
