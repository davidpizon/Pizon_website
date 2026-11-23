using System.Net;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Screens.Hosting;
using Pizon.Web.Client.Screens.Pages;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// Tests for the goodbye and RIP card pages.
/// </summary>
public sealed class SectionPageTests
{
    /// <summary>Goodbye reports the time online, hangs up, logs off and goes back to Connect in place of itself.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GoodbyeHangsUp()
    {
        var h = new Harness().Member();
        h.Session.ConnectedAt = h.Time.Now.AddSeconds(-1234);
        h.Session.ActingAsGuest = true;
        h.Session.SignedOn = true;
        await using var p = new PageHarness(h, "/goodbye");
        PageHarness.AssertAllSlotsBound(p.Show<GoodbyePage>());
        Assert.Equal("/", await p.RunAsync());

        Assert.Contains("GUEST, you were online for 1,234 seconds", p.Plain, StringComparison.Ordinal);
        Assert.Contains("NO CARRIER", p.Plain, StringComparison.Ordinal);
        Assert.Equal(1, h.Presence.Disconnects);
        Assert.False(h.Session.ActingAsGuest);
        Assert.False(h.Session.SignedOn);
        Assert.Equal([Transition.ScrollOff], h.Terminal.Transitions);
        Assert.Equal([("/", true)], h.Navigator.Moves);
        Assert.Contains("0 seconds", GoodbyePage.Farewell("x", TimeSpan.FromSeconds(-5)), StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => GoodbyePage.Farewell(null!, TimeSpan.Zero));
    }

    /// <summary>The RIP card's buttons go where the menu's keys would; anything else goes to the menu.</summary>
    /// <param name="key">The key the card returned.</param>
    /// <param name="expected">The expected address.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(null, "/menu")]
    [InlineData("1", "/bulletins")]
    [InlineData("z", "/menu")]
    public async Task RipCardRoutes(string? key, string expected)
    {
        await using var p = new PageHarness(path: "/rip");
        p.H.Terminal.RipKey = key;
        p.Show<RipPage>();
        Assert.Equal(expected, await p.RunAsync());
        Assert.Equal([(expected, false)], p.H.Navigator.Moves);
    }

    /// <summary>The card's D signs a guest in and its L logs a member out.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RipCardSignsInAndOut()
    {
        var guest = new Harness();
        guest.Session.Me = guest.Api.Me;
        guest.Terminal.RipKey = "d";
        await using (var p = new PageHarness(guest, "/rip"))
        {
            p.Show<RipPage>();
            Assert.Null(await p.RunAsync());
            Assert.Equal([BbsHost.SignInUrl(dev: false)], guest.Navigator.Navigations);
        }

        var member = new Harness().Member();
        member.Terminal.RipKey = "L";
        await using var q = new PageHarness(member, "/rip");
        q.Show<RipPage>();
        Assert.Null(await q.RunAsync());
        Assert.Equal(["/"], member.Navigator.Navigations);
    }
}

/// <summary>
/// Tests for the call: how <see cref="BbsHost"/> starts it from the address and follows the address after.
/// </summary>
public sealed class HostTests
{
    /// <summary>
    /// The page address sets up the call: <c>/</c> dials, <c>/login</c> carries resume, error and a checked
    /// <c>returnUrl</c>, the old addresses still work (moving to <c>/login</c>), and any other route is a link
    /// straight in, joined after asking who is calling.
    /// </summary>
    /// <param name="url">The page address.</param>
    /// <param name="resumed">Whether the call resumes.</param>
    /// <param name="returnTo">The address to open after logging on.</param>
    /// <param name="deepLink">Whether the call skips the dial-up.</param>
    /// <param name="moved">Where the address moved to, if anywhere.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData("http://x/", false, null, false, null)]
    [InlineData("http://x/?resume=login", true, null, false, "/login")]
    [InlineData("http://x/?a=1&LOGIN=FAILED", true, null, false, "/login")]
    [InlineData("http://x/login", false, null, false, null)]
    [InlineData("http://x/login?resume=1&returnUrl=%2Fbulletins%2F2%3Fx%3D1", true, "/bulletins/2", false, null)]
    [InlineData("http://x/login?error=failed&returnUrl=%2F%2Fevil.example", true, null, false, null)]
    [InlineData("http://x/login?returnUrl", false, null, false, null)]
    [InlineData("http://x/Bulletins/", false, null, true, null)]
    [InlineData("http://x/nowhere", false, null, false, null)]
    public async Task StartsFromTheAddress(string url, bool resumed, string? returnTo, bool deepLink, string? moved)
    {
        var h = new Harness();
        h.Navigator.CurrentUri = new Uri(url);
        await h.Host.StartAsync(CancellationToken.None);

        var session = h.Session;
        Assert.Equal(resumed, session.Resumed);
        Assert.Equal(returnTo, session.ReturnTo);
        Assert.Equal(deepLink, session.DeepLink);
        Assert.Equal(url.Contains("failed", StringComparison.OrdinalIgnoreCase) ? BbsHost.LoginFailedMessage : null, session.LoginError);
        Assert.Equal(resumed, h.Terminal.Plain.Contains("CARRIER RESTORED", StringComparison.Ordinal));
        Assert.Equal(resumed || deepLink ? h.Time.Now : default, session.ConnectedAt);
        Assert.Equal(deepLink ? 1 : 0, h.Presence.Connects);
        Assert.Equal(moved is null ? [] : [(moved, true)], h.Navigator.Moves);
    }

    /// <summary>A link straight to a public page draws it with no dial-up, after asking who is calling.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task OpensALinkStraightAway()
    {
        var h = new Harness().Member();
        await using var p = new PageHarness(h, "/settings");
        p.Show<SettingsPage>();
        using var stop = new CancellationTokenSource();
        h.Session.Me = MeResponse.Guest;
        await p.RunAppAsync(stop);

        Assert.Contains("TERMINAL SETTINGS", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Modem ready", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(h.Time.Now, h.Session.ConnectedAt);
        Assert.True(h.Session.SignedOn);
        Assert.Equal("Neo", h.Session.Me.Handle);
        Assert.Equal(1, h.Presence.Connects);
        Assert.Empty(h.Navigator.Moves);
    }

    /// <summary>
    /// A guest's link to a member page goes to login first (replacing the address), then back to the page,
    /// which shows ACCESS DENIED to a caller who stays a guest.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SendsGuestsToLoginFirst()
    {
        var h = new Harness();
        h.Session.Settings = h.Session.Settings with { RipCard = false };
        await using var p = new PageHarness(h, "/login");
        p.Show<LoginPage>();
        h.Navigator.CurrentUri = new Uri("http://localhost/page");
        p.Show<PageSysopPage>();
        using var stop = new CancellationTokenSource();
        h.Navigator.OnNavigate = path => StopAt("/menu", path, stop);
        h.Terminal.Keys(DoorKeys.Enter, "x");
        await p.RunAppAsync(stop);

        Assert.Equal([("/login", true), ("/page", true), ("/menu", false)], h.Navigator.Moves);
        Assert.Contains("ACCESS DENIED", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>A signed-in member who isn't the SysOp gets "file not found" for the SysOp console.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task HidesTheConsoleFromMembers()
    {
        var h = new Harness().Member();
        h.Navigator.CurrentUri = new Uri("http://localhost/sysop/queue");
        using var stop = new CancellationTokenSource();
        h.Terminal.StopWhenEmpty = stop;
        h.Navigator.OnNavigate = path => StopAt("/menu", path, stop);
        h.Terminal.Keys("x");
        await h.Host.RunAsync(stop.Token);

        Assert.Contains("FILE NOT FOUND ON THIS DRIVE", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("MODERATION QUEUE", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal([("/menu", true)], h.Navigator.Moves);
    }

    /// <summary>A signed-in caller without a handle picks one before the linked page opens.</summary>
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
        await p.RunAppAsync(stop);

        Assert.Contains("NEW USER - CHOOSE YOUR HANDLE", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("TERMINAL SETTINGS", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.True(h.Session.ActingAsGuest);
        Assert.Equal([("/handle/new", true), ("/settings", true)], h.Navigator.Moves);
    }

    /// <summary>
    /// Back or Forward to another route stops the page on screen (even in the middle of a key) and runs the
    /// new address's page; the address the BBS ends on is what counts.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task FollowsBackAndForward()
    {
        var h = new Harness();
        await using var p = new PageHarness(h, "/settings");
        p.Show<SettingsPage>();
        h.Navigator.CurrentUri = new Uri("http://localhost/rip");
        p.Show<RipPage>();
        h.Navigator.CurrentUri = new Uri("http://localhost/settings");
        using var stop = new CancellationTokenSource();
        h.Navigator.OnNavigate = path => StopAt("/menu", path, stop);
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
        await p.RunAppAsync(stop);

        Assert.Equal([("/menu", false)], h.Navigator.Moves);
        Assert.Contains("TERMINAL SETTINGS", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>An address with no page goes back to the Connect screen.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task UnknownAddressesDialIn()
    {
        var h = new Harness();
        await using var p = new PageHarness(h, "/");
        p.Show<ConnectPage>();
        h.Navigator.CurrentUri = new Uri("http://localhost/nowhere");
        using var stop = new CancellationTokenSource();
        await p.RunAppAsync(stop);

        Assert.Equal([("/", true)], h.Navigator.Moves);
        Assert.Contains("Modem ready", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>A call back from Discord says CARRIER RESTORED before the login page.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ResumesFromDiscord()
    {
        var h = new Harness();
        h.Api.Me = MeResponse.Guest with { DevLoginEnabled = true };
        await using var p = new PageHarness(h, "/login?resume=1");
        p.Show<LoginPage>();
        h.Terminal.Keys("v");
        using var stop = new CancellationTokenSource();
        await p.RunAppAsync(stop);

        Assert.Contains("CARRIER RESTORED", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(h.Time.Now, h.Session.ConnectedAt);
        Assert.Single(h.Navigator.Navigations);
    }

    /// <summary>A crashing page drops the line back to Connect; cancellation ends the call.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RecoversFromCrashes()
    {
        var h = new Harness();
        await using var p = new PageHarness(h, "/");
        p.Show<ConnectPage>();
        h.Navigator.CurrentUri = new Uri("http://localhost/settings");
        p.Show<SettingsPage>();
        using var stop = new CancellationTokenSource();
        h.Terminal.Keys(FakeTerminal.Crash);
        await p.RunAppAsync(stop);

        Assert.Contains("LINE NOISE - CONNECTION LOST", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("Modem ready", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal([("/", true)], h.Navigator.Moves);
        Assert.True(stop.IsCancellationRequested);
    }

    /// <summary>A crash on the Connect screen itself redraws it in place.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RecoversOnTheConnectScreen()
    {
        var h = new Harness();
        await using var p = new PageHarness(h, "/");
        p.Show<ConnectPage>();
        using var stop = new CancellationTokenSource();
        h.Terminal.Keys(FakeTerminal.Crash);
        await p.RunAppAsync(stop);

        Assert.Contains("LINE NOISE - CONNECTION LOST", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(2, PipeCodes.StripAnsi(h.Terminal.Plain).Split("Modem ready").Length - 1);
        Assert.Empty(h.Navigator.Moves);
    }

    /// <summary>The terminal is only there once attached.</summary>
    [Fact]
    public void NeedsATerminal()
    {
        var host = new BbsHost(new FakeNavigator());
        Assert.Throws<InvalidOperationException>(() => host.Context);
        Assert.False(host.Ready.IsCompleted);
        host.Write("ignored");
        host.SetLineMode(true);
        Assert.Throws<ArgumentNullException>(() => new BbsHost(null!));
        Assert.Throws<ArgumentNullException>(() => host.Attach(null!));
    }

    /// <summary>Ends a test's call once the BBS moves to an address whose page the test doesn't render.</summary>
    private static void StopAt(string target, string path, CancellationTokenSource stop)
    {
        if (path == target)
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
