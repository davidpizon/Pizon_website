using System.Net;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Screens;
using Pizon.Web.Client.Screens.Pages;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// Tests for the goodbye, logout and RIP card screens.
/// </summary>
public sealed class SectionScreenTests
{
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
        await using var p = new PageHarness(h, "/settings");
        p.Show<SettingsPage>();
        using var stop = new CancellationTokenSource();
        h.Session.Me = MeResponse.Guest;
        var start = BbsApp.StartScreen(h.Navigator.CurrentUri, h.Session);
        await p.RunAppAsync(start, stop);

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
        await using var p = new PageHarness(h, "/page");
        p.Show<PageSysopPage>();
        using var stop = new CancellationTokenSource();
        h.Navigator.OnNavigate = path => StopAtMenu(path, stop);
        h.Terminal.Keys(DoorKeys.Enter, "x");
        var start = BbsApp.StartScreen(h.Navigator.CurrentUri, h.Session);
        await p.RunAppAsync(start, stop);

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
        await using var p = new PageHarness(h, "/handle/new");
        p.Show<NewUserHandlePage>();
        h.Navigator.CurrentUri = new Uri("http://localhost/settings");
        p.Show<SettingsPage>();
        using var stop = new CancellationTokenSource();
        h.Terminal.Keys(DoorKeys.Escape);
        var start = BbsApp.StartScreen(h.Navigator.CurrentUri, h.Session);
        await p.RunAppAsync(start, stop);

        Assert.Contains("NEW USER - CHOOSE YOUR HANDLE", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("TERMINAL SETTINGS", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.True(h.Session.ActingAsGuest);
        Assert.Equal([("/handle/new", true), ("/settings", true)], h.Navigator.Moves);
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
        await using var p = new PageHarness(h, "/settings");
        p.Show<SettingsPage>();
        using var stop = new CancellationTokenSource();
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
        await p.RunAppAsync(ScreenId.Settings, stop);

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
        await h.App().RunAsync(h.Session, ScreenId.Login, stop.Token);
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
