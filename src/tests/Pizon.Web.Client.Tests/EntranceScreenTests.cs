using Pizon.Doors.Sdk;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// Tests for the Connect, Handshake and Welcome screens.
/// </summary>
public sealed class EntranceScreenTests
{
    /// <summary>S toggles and saves sound; C dials and stamps the connect time.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ConnectTogglesSoundThenDials()
    {
        var h = new Harness();
        h.Terminal.Keys("x", "S", "C");
        var next = await new ConnectScreen(h.Context).RunAsync(h.Session, CancellationToken.None);

        Assert.Equal(ScreenId.Handshake, next);
        Assert.True(h.Session.Settings.Sound);
        Assert.Contains("\"Sound\":true", h.Terminal.Stored[TerminalSettings.StorageKey], StringComparison.Ordinal);
        Assert.Single(h.Terminal.Applied);
        Assert.Equal(h.Time.Now, h.Session.ConnectedAt);
        Assert.Contains("CONNECT", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(ScreenId.Connect, new ConnectScreen(h.Context).Id);
    }

    /// <summary>Enter also dials.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task EnterDials()
    {
        var h = new Harness();
        h.Terminal.Keys(DoorKeys.Enter);
        Assert.Equal(ScreenId.Handshake, await new ConnectScreen(h.Context).RunAsync(h.Session, CancellationToken.None));
    }

    /// <summary>A silent handshake waits its full length, then plays the sting.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SilentHandshakeRunsToTheEnd()
    {
        var h = new Harness();
        var screen = new HandshakeScreen(h.Context);
        Assert.Equal(ScreenId.Welcome, await screen.RunAsync(h.Session, CancellationToken.None));

        Assert.Equal(ScreenId.Handshake, screen.Id);
        Assert.Equal(0, h.Terminal.AudioStops);
        Assert.Equal(HandshakeScreen.SilentDuration + TimeSpan.FromMilliseconds(400), h.Terminal.Delayed);
        Assert.Equal(HandshakeScreen.StingUrl, Assert.Single(h.Terminal.Animations).Url);
        Assert.Contains("CONNECT 14400", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(1, h.Terminal.Discards);
    }

    /// <summary>Skipping stops the audio and jumps to CONNECT.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SkippedHandshakeStopsAudio()
    {
        var h = new Harness();
        h.Terminal.Skip = true;
        h.Terminal.Handshake = TimeSpan.FromSeconds(10);
        await new HandshakeScreen(h.Context).RunAsync(h.Session, CancellationToken.None);

        Assert.Equal(1, h.Terminal.AudioStops);
        Assert.DoesNotContain("RINGING", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(1) + TimeSpan.FromMilliseconds(400), h.Terminal.Delayed);
    }

    /// <summary>The welcome sprays the banner (or a fallback) with the status header.</summary>
    /// <param name="hasArt">Whether the banner file exists.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WelcomeSpraysTheBanner(bool hasArt)
    {
        var h = new Harness();
        if (hasArt)
        {
            h.Terminal.Art[WelcomeScreen.BannerUrl] = "GRAFFITI";
        }

        var screen = new WelcomeScreen(h.Context);
        Assert.Equal(ScreenId.Login, await screen.RunAsync(h.Session, CancellationToken.None));
        Assert.Equal(ScreenId.Welcome, screen.Id);
        Assert.Equal([Transition.Spray], h.Terminal.Transitions);
        Assert.Contains(hasArt ? "GRAFFITI" : "P I Z O N", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("SysOp: David Pizon", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("Mood: Calm", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("Tag", h.Terminal.Plain, StringComparison.Ordinal);
    }
}

/// <summary>
/// Tests for the login, sign-in and handle screens.
/// </summary>
public sealed class LoginScreenTests
{
    /// <summary>A guest sees the options, ignores stray keys and connects with Enter.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GuestConnects()
    {
        var h = new Harness();
        h.Terminal.Keys("x", "V", DoorKeys.Enter);
        var screen = new LoginScreen(h.Context);
        Assert.Equal(ScreenId.RipCard, await screen.RunAsync(h.Session, CancellationToken.None));

        Assert.Equal(ScreenId.Login, screen.Id);
        Assert.Contains("Sign in with Discord", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Dev sign-in", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("Connecting as GUEST", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal(1, h.Presence.Connects);
        Assert.Empty(h.Navigator.Navigations);
    }

    /// <summary>D goes to Discord sign-in; G also connects as guest (to the menu when the RIP card is off).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DiscordAndGKeys()
    {
        var h = new Harness();
        h.Terminal.Keys("d");
        Assert.Equal(ScreenId.SignIn, await new LoginScreen(h.Context).RunAsync(h.Session, CancellationToken.None));

        h.Session.Settings = h.Session.Settings with { RipCard = false };
        h.Terminal.Keys("g");
        Assert.Equal(ScreenId.MainMenu, await new LoginScreen(h.Context).RunAsync(h.Session, CancellationToken.None));
    }

    /// <summary>The dev sign-in leaves the page; the Discord key does nothing when Discord is off.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DevSignInLeavesThePage()
    {
        var h = new Harness();
        h.Api.Me = MeResponse.Guest with { DevLoginEnabled = true };
        h.Session.LoginError = BbsApp.LoginFailedMessage;
        h.Terminal.Keys("D", "v");
        Assert.Equal(ScreenId.Exit, await new LoginScreen(h.Context).RunAsync(h.Session, CancellationToken.None));

        Assert.Equal([LoginScreen.SignInUrl(dev: true)], h.Navigator.Navigations);
        Assert.Contains(BbsApp.LoginFailedMessage, h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Null(h.Session.LoginError);
        Assert.Contains("Dev sign-in", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>Sign-in URLs come back to the resume address.</summary>
    [Fact]
    public void SignInUrlsResume()
    {
        Assert.Equal("auth/discord?returnUrl=%2F%3Fresume%3Dlogin", LoginScreen.SignInUrl(dev: false));
        Assert.Equal("auth/dev?user=tester&returnUrl=%2F%3Fresume%3Dlogin", LoginScreen.SignInUrl(dev: true));
    }

    /// <summary>A signed-in caller without a handle picks one first.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NewUserPicksAHandle()
    {
        var h = new Harness();
        h.Api.Me = new MeResponse(true, null, "Neo", 10, false, true, false);
        Assert.Equal(ScreenId.NewUserHandle, await new LoginScreen(h.Context).RunAsync(h.Session, CancellationToken.None));
    }

    /// <summary>A returning member is welcomed back; the SysOp asked for the console gets it.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task MembersAreWelcomedBack()
    {
        var h = new Harness().Member(sysop: true);
        h.Session.SysopRequested = true;
        Assert.Equal(ScreenId.SysopConsole, await new LoginScreen(h.Context).RunAsync(h.Session, CancellationToken.None));
        Assert.False(h.Session.SysopRequested);
        Assert.Contains("WELCOME BACK, SysOp1", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>Discord sign-in navigates away.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SignInScreenNavigates()
    {
        var h = new Harness();
        var screen = new SignInScreen(h.Context);
        Assert.Equal(ScreenId.Exit, await screen.RunAsync(h.Session, CancellationToken.None));
        Assert.Equal(ScreenId.SignIn, screen.Id);
        Assert.Equal([LoginScreen.SignInUrl(dev: false)], h.Navigator.Navigations);
    }

    /// <summary>
    /// A new user tries a taken handle, then a malformed one, then a good one, and is logged in.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NewUserHandleRetriesUntilAccepted()
    {
        var h = new Harness();
        h.Api.Me = new MeResponse(true, null, "Neo", 10, false, true, false);
        h.Session.Me = h.Api.Me;
        h.Api.HandleResults.Enqueue(new ApiResult(ApiStatus.Invalid, "taken"));
        h.Terminal
            .Keys(DoorKeys.Enter)
            .Keys(DoorKeys.Backspace, DoorKeys.Backspace, DoorKeys.Backspace).Type("ab")
            .Type("c");

        var screen = new HandleScreen(h.Context, isNewUser: true);
        Assert.Equal(ScreenId.NewUserHandle, screen.Id);
        Assert.Equal(ScreenId.RipCard, await screen.RunAsync(h.Session, CancellationToken.None));

        Assert.Equal(["handle:Neo", "handle:abc"], h.Api.Calls);
        Assert.Contains("REJECTED: TAKEN", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("won't fit through the phone line", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("Handle registered: abc", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal("abc", h.Session.Me.Handle);
        Assert.Equal(1, h.Presence.Connects);
    }

    /// <summary>A new user who presses ESC stays a guest for the call.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NewUserEscapeStaysGuest()
    {
        var h = new Harness();
        h.Session.Me = new MeResponse(true, null, null, 10, false, true, false);
        h.Session.Settings = h.Session.Settings with { RipCard = false };
        h.Terminal.Keys(DoorKeys.Escape);
        Assert.Equal(ScreenId.MainMenu, await new HandleScreen(h.Context, isNewUser: true).RunAsync(h.Session, CancellationToken.None));
        Assert.True(h.Session.ActingAsGuest);
        Assert.Equal("GUEST", h.Session.Handle);
    }

    /// <summary>Guests can't change a handle.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GuestsCannotChangeHandle()
    {
        var h = new Harness();
        h.Terminal.Keys("x");
        var screen = new HandleScreen(h.Context, isNewUser: false);
        Assert.Equal(ScreenId.ChangeHandle, screen.Id);
        Assert.Equal(ScreenId.MainMenu, await screen.RunAsync(h.Session, CancellationToken.None));
        Assert.Contains("ACCESS DENIED", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>A member changes their handle, or cancels.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task MemberChangesHandle()
    {
        var h = new Harness().Member();
        h.Terminal.Keys(DoorKeys.Backspace, DoorKeys.Backspace, DoorKeys.Backspace).Type("Trinity").Keys("x");
        Assert.Equal(ScreenId.MainMenu, await new HandleScreen(h.Context, isNewUser: false).RunAsync(h.Session, CancellationToken.None));
        Assert.Equal(["handle:Trinity"], h.Api.Calls);

        h.Terminal.Keys(DoorKeys.Escape);
        Assert.Equal(ScreenId.MainMenu, await new HandleScreen(h.Context, isNewUser: false).RunAsync(h.Session, CancellationToken.None));
        Assert.Single(h.Api.Calls);
    }
}

/// <summary>
/// Tests for the main menu.
/// </summary>
public sealed class MainMenuScreenTests
{
    /// <summary>Menu keys route to their screens, regardless of case.</summary>
    /// <param name="key">The key.</param>
    /// <param name="expected">The expected screen.</param>
    [Theory]
    [InlineData("1", ScreenId.Bulletins)]
    [InlineData("2", ScreenId.MessageBoard)]
    [InlineData("3", ScreenId.FileAreas)]
    [InlineData("4", ScreenId.Doors)]
    [InlineData("5", ScreenId.UserList)]
    [InlineData("p", ScreenId.PageSysop)]
    [InlineData("H", ScreenId.ChangeHandle)]
    [InlineData("s", ScreenId.Settings)]
    [InlineData("R", ScreenId.RipCard)]
    [InlineData("g", ScreenId.Goodbye)]
    public void RoutesKeys(string key, ScreenId expected) => Assert.Equal(expected, MainMenuScreen.Route(key, new BbsSession()));

    /// <summary>D, L and * depend on who is calling; other keys do nothing.</summary>
    [Fact]
    public void CallerDependentKeys()
    {
        var guest = new BbsSession { Me = MeResponse.Guest with { DiscordEnabled = true } };
        var member = new Harness().Member().Session;
        var sysop = new Harness().Member(sysop: true).Session;

        Assert.Equal(ScreenId.SignIn, MainMenuScreen.Route("D", guest));
        Assert.Null(MainMenuScreen.Route("D", member));
        Assert.Null(MainMenuScreen.Route("D", new BbsSession()));
        Assert.Null(MainMenuScreen.Route("L", guest));
        Assert.Equal(ScreenId.Logout, MainMenuScreen.Route("L", member));
        Assert.Null(MainMenuScreen.Route("*", member));
        Assert.Equal(ScreenId.SysopConsole, MainMenuScreen.Route("*", sysop));
        Assert.Null(MainMenuScreen.Route("z", member));
        Assert.Null(MainMenuScreen.Route(DoorKeys.Enter, member));
    }

    /// <summary>The menu shows the options this caller can use.</summary>
    [Fact]
    public void RendersPerCaller()
    {
        var guest = Ui.P(MainMenuScreen.Render(new BbsSession()));
        Assert.Contains("Sign in with Discord", guest, StringComparison.Ordinal);
        Assert.DoesNotContain("Log out", guest, StringComparison.Ordinal);

        var sysop = MainMenuScreen.Render(new Harness().Member(sysop: true).Session);
        Assert.Contains("Change Handle", sysop, StringComparison.Ordinal);
        Assert.Contains("Log out of Discord", sysop, StringComparison.Ordinal);
        Assert.Contains("SysOp Console", sysop, StringComparison.Ordinal);
    }

    /// <summary>The menu draws, keeps the online line live, hints on ESC and leaves on a menu key.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RunsTheMenu()
    {
        var h = new Harness();
        h.Presence.Set(1, 2);
        h.Terminal.OnRead = k =>
        {
            if (k == "z")
            {
                h.Presence.Set(1, 2, 3);
            }
        };
        h.Terminal.Keys(DoorKeys.Escape, "z", "1");
        var screen = new MainMenuScreen(h.Context);
        Assert.Equal(ScreenId.Bulletins, await screen.RunAsync(h.Session, CancellationToken.None));

        Assert.Equal(ScreenId.MainMenu, screen.Id);
        Assert.Equal([Transition.Draw, Transition.Undraw], h.Terminal.Transitions);
        Assert.Contains("2 callers online", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("3 callers online · nodes busy: 1,2,3", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("To log off, press G", h.Terminal.Plain, StringComparison.Ordinal);

        h.Presence.Set(1);
        Assert.DoesNotContain("1 callers online", h.Terminal.Plain, StringComparison.Ordinal);
    }
}

/// <summary>
/// Tests for the shared UI helpers.
/// </summary>
public sealed class UiTests
{
    /// <summary>Headers and status lines carry the expected text.</summary>
    [Fact]
    public void BuildsHeadersAndStatus()
    {
        Assert.Contains("MAIN MENU", Ui.Header("Main menu"), StringComparison.Ordinal);
        var h = new Harness().Member();
        var status = Ui.StatusHeader(h.Session, h.Presence, "David", "Calm", "Grid", new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero));
        Assert.Contains("SUN NOV 15 2026", status, StringComparison.Ordinal);
        Assert.Contains("Neo", status, StringComparison.Ordinal);
        Assert.Equal("1,024", Ui.Size(1024));
        Assert.Contains("key:Q", Ui.Item("Q", "Quit"), StringComparison.Ordinal);
        Assert.Contains("[Enter]", Ui.Item(DoorKeys.Enter, "Go", "Enter"), StringComparison.Ordinal);
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

    /// <summary>Paging stops on Q and continues on Enter.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PagesLines()
    {
        var lines = new[] { "a", "b", "c", "d", "e" };
        var t = new FakeTerminal().Keys(DoorKeys.Enter, DoorKeys.Enter);
        Assert.True(await Ui.PageAsync(t, lines, 2, CancellationToken.None));
        Assert.Contains("e", t.Plain, StringComparison.Ordinal);

        var q = new FakeTerminal().Keys("q");
        Assert.False(await Ui.PageAsync(q, lines, 2, CancellationToken.None));
        Assert.DoesNotContain("c", q.Plain.Replace("-- more --", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

        var esc = new FakeTerminal().Keys(DoorKeys.Escape);
        Assert.False(await Ui.PageAsync(esc, lines, 2, CancellationToken.None));
    }

    /// <summary>The online line is blank for a lone caller.</summary>
    [Fact]
    public void OnlineLineHidesForOneCaller()
    {
        var p = new FakePresence();
        Assert.DoesNotContain("online", Ui.OnlineLine(p), StringComparison.Ordinal);
    }
}
