using Bunit;
using Pizon.Doors.Sdk;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens;
using Pizon.Web.Client.Screens.Hosting;
using Pizon.Web.Client.Screens.Pages;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// Tests for the way in: the Connect page and its dial-up handshake.
/// </summary>
public sealed class ConnectPageTests
{
    /// <summary>
    /// S toggles and saves sound (redrawing just that slot); C dials, stamps the connect time and replaces the
    /// address with the login screen.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task TogglesSoundThenDials()
    {
        await using var p = new PageHarness(path: "/");
        var page = p.Show<ConnectPage>();
        PageHarness.AssertAllSlotsBound(page);
        Assert.Equal("/login", await p.RunAsync("x", "S", "C"));

        var h = p.H;
        Assert.True(h.Session.Settings.Sound);
        Assert.Contains("\"Sound\":true", h.Terminal.Stored[TerminalSettings.StorageKey], StringComparison.Ordinal);
        Assert.Single(h.Terminal.Applied);
        Assert.Equal(h.Time.Now, h.Session.ConnectedAt);
        Assert.Contains("Modem ready", p.Plain, StringComparison.Ordinal);
        Assert.Contains("CONNECT 14400", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/login", true)], h.Navigator.Moves);
        p.AssertNoPlaceholders();
    }

    /// <summary>Enter also dials.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task EnterDials()
    {
        await using var p = new PageHarness(path: "/");
        p.Show<ConnectPage>();
        Assert.Equal("/login", await p.RunAsync(DoorKeys.Enter));
    }

    /// <summary>A silent handshake waits its full length, then plays the sting.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SilentHandshakeRunsToTheEnd()
    {
        var h = new Harness();
        await ConnectPage.HandshakeAsync(h.Terminal, CancellationToken.None);

        Assert.Equal(0, h.Terminal.AudioStops);
        Assert.Equal(ConnectPage.SilentDuration + TimeSpan.FromMilliseconds(400), h.Terminal.Delayed);
        Assert.Equal(ConnectPage.StingUrl, Assert.Single(h.Terminal.Animations).Url);
        Assert.Contains("RINGING", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("CONNECT 14400", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(1, h.Terminal.Discards);
        await Assert.ThrowsAsync<ArgumentNullException>(() => ConnectPage.HandshakeAsync(null!, CancellationToken.None));
    }

    /// <summary>Skipping stops the audio and jumps to CONNECT.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SkippedHandshakeStopsAudio()
    {
        var h = new Harness();
        h.Terminal.Skip = true;
        h.Terminal.Handshake = TimeSpan.FromSeconds(10);
        await ConnectPage.HandshakeAsync(h.Terminal, CancellationToken.None);

        Assert.Equal(1, h.Terminal.AudioStops);
        Assert.DoesNotContain("RINGING", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(1) + TimeSpan.FromMilliseconds(400), h.Terminal.Delayed);
    }
}

/// <summary>
/// Tests for the login page: the sprayed banner and the ways in.
/// </summary>
public sealed class LoginPageTests
{
    /// <summary>
    /// The banner is sprayed on with the status lines filled in; a guest sees the options, stray keys do
    /// nothing, and Enter connects as GUEST, joins presence and goes on to the RIP card in place of the login.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GuestConnects()
    {
        await using var p = new PageHarness(path: "/login");
        var page = p.Show<LoginPage>();
        PageHarness.AssertAllSlotsBound(page);
        Assert.Equal("/rip", await p.RunAsync("x", "V", DoorKeys.Enter));

        var h = p.H;
        Assert.Equal([Transition.Spray], h.Terminal.Transitions);
        Assert.Contains("B · B · S", p.Plain, StringComparison.Ordinal);
        Assert.Contains("░", p.Plain, StringComparison.Ordinal);
        Assert.Contains("SysOp:", p.Plain, StringComparison.Ordinal);
        Assert.Contains("David Pizon", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Calm", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Tag", p.Plain, StringComparison.Ordinal);
        Assert.Contains("How do you wish to connect?", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Sign in with Discord", p.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Dev sign-in", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Connecting as GUEST", p.Plain, StringComparison.Ordinal);
        Assert.Equal(1, h.Presence.Connects);
        Assert.True(h.Session.SignedOn);
        Assert.True(h.Terminal.Discards > 0);
        Assert.Empty(h.Navigator.Navigations);
        Assert.Equal([("/rip", true)], h.Navigator.Moves);
        p.AssertNoPlaceholders();
    }

    /// <summary>D leaves for Discord sign-in; G also connects as guest (to the menu when the RIP card is off).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DiscordAndGKeys()
    {
        await using (var p = new PageHarness(path: "/login"))
        {
            p.Show<LoginPage>();
            Assert.Null(await p.RunAsync("d"));
            Assert.Equal([BbsHost.SignInUrl(dev: false)], p.H.Navigator.Navigations);
            Assert.Contains("Discord switchboard", p.Plain, StringComparison.Ordinal);
        }

        await using var q = new PageHarness(path: "/login");
        q.H.Session.Settings = q.H.Session.Settings with { RipCard = false };
        q.Show<LoginPage>();
        Assert.Equal("/menu", await q.RunAsync("g"));
    }

    /// <summary>
    /// The dev sign-in leaves the page, carrying the address to come back to; the Discord key does nothing
    /// when Discord is off; a failed sign-in is shown once.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DevSignInLeavesThePage()
    {
        var h = new Harness();
        h.Api.Me = MeResponse.Guest with { DevLoginEnabled = true };
        h.Session.LoginError = BbsHost.LoginFailedMessage;
        h.Session.ReturnTo = "/page";
        await using var p = new PageHarness(h, "/login");
        p.Show<LoginPage>();
        Assert.Null(await p.RunAsync("D", "v"));

        Assert.Equal([BbsHost.SignInUrl(dev: true, "/page")], h.Navigator.Navigations);
        Assert.Contains(BbsHost.LoginFailedMessage, p.Plain, StringComparison.Ordinal);
        Assert.Null(h.Session.LoginError);
        Assert.Contains("Dev sign-in", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Dialing the dev line", p.Plain, StringComparison.Ordinal);
    }

    /// <summary>Sign-in URLs come back to the login screen, carrying the address to open afterwards.</summary>
    [Fact]
    public void SignInUrlsResume()
    {
        Assert.Equal("auth/discord?returnUrl=%2Flogin%3Fresume%3D1", BbsHost.SignInUrl(dev: false));
        Assert.Equal("auth/dev?user=tester&returnUrl=%2Flogin%3Fresume%3D1%26returnUrl%3D%252Fpage", BbsHost.SignInUrl(dev: true, "/page"));
    }

    /// <summary>A signed-in caller without a handle picks one first, without the banner being drawn.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NewUserPicksAHandle()
    {
        var h = new Harness();
        h.Api.Me = new MeResponse(true, null, "Neo", 10, false, true, false);
        await using var p = new PageHarness(h, "/login");
        var cut = p.Bunit.Render<LoginPage>();
        cut.WaitForAssertion(() => Assert.Equal([("/handle/new", true)], h.Navigator.Moves));
        Assert.Equal(0, h.Host.ScreenCount);
    }

    /// <summary>
    /// A returning member sees the banner with WELCOME BACK and no choices, and goes straight on to the
    /// address they were linked to (the SysOp to the console).
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task MembersAreWelcomedBack()
    {
        var h = new Harness().Member(sysop: true);
        h.Session.ReturnTo = "/sysop/queue";
        await using var p = new PageHarness(h, "/login");
        p.Show<LoginPage>();
        Assert.Equal("/sysop/queue", await p.RunAsync());

        Assert.Null(h.Session.ReturnTo);
        Assert.True(h.Session.SignedOn);
        Assert.Contains("WELCOME BACK, SysOp1", p.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Connect as GUEST", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/sysop/queue", true)], h.Navigator.Moves);
    }

    /// <summary>Logging on goes to the linked address, but never back to the way in.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LogOnIgnoresTheWayIn()
    {
        var h = new Harness();
        h.Session.Settings = h.Session.Settings with { RipCard = false };
        foreach (var returnTo in new[] { "/login", "/", "/handle/new", "//evil.example" })
        {
            h.Session.ReturnTo = returnTo;
            await h.Host.LogOnAsync();
            Assert.Equal(("/menu", true), h.Navigator.Moves[^1]);
        }

        h.Session.ReturnTo = "/bulletins/2";
        await h.Host.LogOnAsync();
        Assert.Equal(("/bulletins/2", true), h.Navigator.Moves[^1]);
        Assert.Equal(5, h.Presence.Connects);
    }

    /// <summary>The options list the ways in this caller has; a signed-in caller has none.</summary>
    [Fact]
    public void ListsTheWaysIn()
    {
        var all = LoginPage.Options(MeResponse.Guest with { DiscordEnabled = true, DevLoginEnabled = true }, signedIn: false);
        Assert.Contains("|[D]", all, StringComparison.Ordinal);
        Assert.Contains("|[V]", all, StringComparison.Ordinal);
        Assert.Contains("|[Enter]", all, StringComparison.Ordinal);
        Assert.Equal(Ui.ItemPipe(DoorKeys.Enter, "Connect as GUEST"), LoginPage.Options(MeResponse.Guest, signedIn: false));
        Assert.Empty(LoginPage.Options(MeResponse.Guest, signedIn: true));
        Assert.Throws<ArgumentNullException>(() => LoginPage.Options(null!, signedIn: false));
        Assert.Equal("SUN NOV 15 2026", LoginPage.Date(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero)));
    }
}

/// <summary>
/// Tests for the RIP card's button routing (the same addresses as the main menu's hotkeys).
/// </summary>
public sealed class RipRouteTests
{
    /// <summary>Card buttons go to their addresses, regardless of case.</summary>
    /// <param name="key">The key.</param>
    /// <param name="expected">The expected address.</param>
    [Theory]
    [InlineData("1", "/bulletins")]
    [InlineData("2", "/board")]
    [InlineData("3", "/files")]
    [InlineData("4", "/doors")]
    [InlineData("5", "/users")]
    [InlineData("p", "/page")]
    [InlineData("H", "/handle")]
    [InlineData("s", "/settings")]
    [InlineData("g", "/goodbye")]
    public void RoutesKeys(string key, string expected) => Assert.Equal(expected, RipPage.Route(key, new BbsSession()));

    /// <summary>* is only for the SysOp; other keys (and none) go back to the menu.</summary>
    [Fact]
    public void CallerDependentKeys()
    {
        var member = new Harness().Member().Session;
        var sysop = new Harness().Member(sysop: true).Session;
        Assert.Null(RipPage.Route("*", member));
        Assert.Equal("/sysop", RipPage.Route("*", sysop));
        Assert.Null(RipPage.Route("R", member));
        Assert.Null(RipPage.Route("z", member));
        Assert.Null(RipPage.Route(null, member));
        Assert.Null(RipPage.Route(DoorKeys.Enter, member));
        Assert.Throws<ArgumentNullException>(() => RipPage.Route("1", null!));
    }
}

/// <summary>
/// Tests for the shared UI helpers.
/// </summary>
public sealed class UiTests
{
    /// <summary>The small formatting helpers carry the expected text.</summary>
    [Fact]
    public void BuildsItems()
    {
        var h = new Harness().Member();
        Assert.Equal("|[Q]|14[Q]|07 Quit|07|]", Ui.ItemPipe("Q", "Quit"));
        Assert.Contains("Neo", Ui.AccessDenied(h.Session), StringComparison.Ordinal);
        Assert.Equal("1,024", Ui.Size(1024));
        Assert.Matches(@"^\d\d/\d\d/\d\d \d\d:\d\d$", Ui.Stamp(DateTimeOffset.UnixEpoch));
    }

    /// <summary>Each failure status has its own message.</summary>
    /// <param name="status">The status.</param>
    /// <param name="expected">Text expected in the message.</param>
    [Theory]
    [InlineData(ApiStatus.Unauthorized, "SIGN IN FIRST")]
    [InlineData(ApiStatus.Forbidden, "THE SYSOP HAS SPOKEN")]
    [InlineData(ApiStatus.RateLimited, "SPEED RACER")]
    [InlineData(ApiStatus.Invalid, "REJECTED: INVALID INPUT")]
    [InlineData(ApiStatus.Failed, "LINE NOISE")]
    public void DescribesFailures(ApiStatus status, string expected) =>
        Assert.Contains(expected, Ui.Failure(new ApiResult(status)), StringComparison.Ordinal);

    /// <summary>The online text is blank for a lone caller.</summary>
    [Fact]
    public void OnlineTextHidesForOneCaller()
    {
        var p = new FakePresence();
        Assert.Equal(string.Empty, Ui.OnlineText(p));
    }
}
