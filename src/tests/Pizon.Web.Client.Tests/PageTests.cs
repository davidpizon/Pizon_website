using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens;
using Pizon.Web.Client.Screens.Components;
using Pizon.Web.Client.Screens.Hosting;
using Pizon.Web.Client.Screens.Pages;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Services;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// Renders one screen page with bUnit over a <see cref="Harness"/>, then runs <see cref="BbsHost"/>'s pump
/// over the scripted terminal, the way the BBS does once the router has shown the page.
/// </summary>
internal sealed partial class PageHarness : IAsyncDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PageHarness"/> class.
    /// </summary>
    /// <param name="harness">The fakes (a fresh guest harness by default).</param>
    /// <param name="path">The address the page is shown at.</param>
    public PageHarness(Harness? harness = null, string path = "/menu")
    {
        H = harness ?? new Harness();
        H.Navigator.CurrentUri = new Uri(new Uri("http://localhost/"), path);
        Bunit.JSInterop.Mode = JSRuntimeMode.Loose;
        Bunit.Services.AddSingleton(H.Host);
        Bunit.Services.AddSingleton<IBbsApi>(H.Api);
        Bunit.Services.AddSingleton(H.Content);
        Bunit.Services.AddSingleton<IPresence>(H.Presence);
        Bunit.Services.AddSingleton<IExternalNavigator>(H.Navigator);
    }

    /// <summary>Gets the fakes.</summary>
    public Harness H { get; }

    /// <summary>Gets the bUnit context.</summary>
    public BunitContext Bunit { get; } = new();

    /// <summary>Gets everything the terminal showed, as plain text.</summary>
    public string Plain => H.Terminal.Plain;

    /// <summary>
    /// Renders a page and waits until its screen has registered with the host.
    /// </summary>
    /// <typeparam name="T">The page.</typeparam>
    /// <param name="parameters">The page's parameters.</param>
    /// <returns>The rendered page.</returns>
    public IRenderedComponent<T> Show<T>(Action<ComponentParameterCollectionBuilder<T>>? parameters = null)
        where T : IComponent
    {
        var cut = parameters is null ? Bunit.Render<T>() : Bunit.Render(parameters);
        cut.WaitForState(() => H.Host.ScreenCount > 0);
        return cut;
    }

    /// <summary>
    /// Runs the host's pump for a screen until the scripted keys run out or a page leaves for a screen.
    /// </summary>
    /// <param name="screen">The screen whose pages run.</param>
    /// <param name="keys">The keys to press.</param>
    /// <returns>The screen a page left for, or <see langword="null"/> when the keys ran out.</returns>
    public async Task<ScreenId?> RunAsync(ScreenId screen, params string[] keys)
    {
        H.Terminal.Keys(keys);
        using var stop = new CancellationTokenSource();
        H.Terminal.StopWhenEmpty = stop;
        try
        {
            return await Bunit.Renderer.Dispatcher.InvokeAsync(() => H.Host.RunPagesAsync(screen, stop.Token));
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every text slot in the page's art is bound by the page (the cursor slot is where the prompt goes, and
    /// animation slots wait for phase 6).
    /// </summary>
    /// <param name="page">The rendered page.</param>
    /// <typeparam name="T">The page.</typeparam>
    public static void AssertAllSlotsBound<T>(IRenderedComponent<T> page)
        where T : class, IComponent
    {
        var screen = page.FindComponent<BbsScreen>().Instance;
        var expected = screen.Art.Fields.Where(f => f.Kind == SlotKind.Field && f.Name != ScreenArt.CursorName).Select(f => f.Name).Order(StringComparer.Ordinal);
        Assert.Equal(expected, screen.BoundSlots.Select(f => f.Name).Distinct().Order(StringComparer.Ordinal));
    }

    /// <summary>The terminal never receives template syntax: only finished ANSI.</summary>
    public void AssertNoPlaceholders() => Assert.DoesNotMatch(PlaceholderRegex(), Plain);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Bunit.DisposeAsync();

    [GeneratedRegex(@"@(\?|\^|\[|ANIM:|[A-Z_]{2,}@)")]
    private static partial Regex PlaceholderRegex();
}

/// <summary>
/// Tests for the main menu page.
/// </summary>
public sealed class MainMenuPageTests
{
    /// <summary>
    /// The menu draws behind a moving cursor, sends only the ONLINE slot when presence changes (one positioned
    /// write), hints on Escape, and on a menu key echoes it, undraws and goes to the address. Once left, it
    /// never writes again.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DrawsKeepsOnlineLiveAndLeaves()
    {
        await using var p = new PageHarness();
        p.H.Presence.Set(1, 2);
        p.Show<MainMenuPage>();
        p.H.Terminal.OnRead = k =>
        {
            if (k == "z")
            {
                p.H.Presence.Set(1, 2, 3);
            }
        };
        Assert.Null(await p.RunAsync(ScreenId.MainMenu, DoorKeys.Escape, "z", "1"));

        Assert.Equal([Transition.Draw, Transition.Undraw], p.H.Terminal.Transitions);
        Assert.Contains("2 callers online", p.Plain, StringComparison.Ordinal);
        var live = Assert.Single(p.H.Terminal.Writes, w => w.Contains("callers online", StringComparison.Ordinal));
        Assert.StartsWith(Ansi.Reset + Ansi.MoveTo(25, 1), live, StringComparison.Ordinal);
        Assert.Contains("3 callers online · nodes busy: 1,2,3", PipeCodes.StripAnsi(live), StringComparison.Ordinal);
        Assert.Contains("To log off, press G", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/bulletins", false)], p.H.Navigator.Moves);
        Assert.Contains(p.H.Terminal.Writes, w => w == "1");
        p.AssertNoPlaceholders();

        var writes = p.H.Terminal.Writes.Count;
        await p.Bunit.Renderer.Dispatcher.InvokeAsync(() => p.H.Presence.Set(1, 4));
        Assert.Equal(writes, p.H.Terminal.Writes.Count);
    }

    /// <summary>The menu shows the options this caller can use, in the terminal and in the mirror.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ShowsTheCallersOptions()
    {
        await using (var guest = new PageHarness())
        {
            var cut = guest.Show<MainMenuPage>();
            Assert.Contains("Sign in with Discord", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Log out", cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("a[href='/sysop']"));
        }

        await using var sysop = new PageHarness(new Harness().Member(sysop: true));
        var page = sysop.Show<MainMenuPage>();
        await sysop.RunAsync(ScreenId.MainMenu);
        Assert.Contains("Change Handle", sysop.Plain, StringComparison.Ordinal);
        Assert.Contains("Log out of Discord", sysop.Plain, StringComparison.Ordinal);
        Assert.Contains("SysOp Console", sysop.Plain, StringComparison.Ordinal);
        Assert.Contains("SysOp1", sysop.Plain, StringComparison.Ordinal);
        Assert.Equal("[*] SysOp Console", page.Find("a[href='/sysop']").TextContent);
    }

    /// <summary>The menu's hotkeys go exactly where <see cref="MainMenuPage.Route"/> says, for every kind of caller.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task HotkeysAgreeWithTheRouteMap()
    {
        var guest = new Harness();
        guest.Session.Me = guest.Api.Me;
        foreach (var h in new[] { guest, new Harness(), new Harness().Member(), new Harness().Member(sysop: true) })
        {
            await using var p = new PageHarness(h);
            var screen = p.Show<MainMenuPage>().FindComponent<BbsScreen>().Instance;
            foreach (var key in "12345phsrgdl*zq".Select(c => c.ToString()).Append(DoorKeys.Enter))
            {
                var hotkey = screen.Hotkeys.FirstOrDefault(k => k.Matches(key));
                ScreenId? actual = hotkey is null ? null : hotkey.Screen ?? (RouteTable.TryResolve(hotkey.Href, out var r) ? r.Screen : null);
                Assert.Equal(MainMenuPage.Route(key, h.Session), actual);
            }
        }
    }

    /// <summary>Keys for screens without an address (log out, sign in) leave the pages for that screen.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LeavesForScreensWithoutAnAddress()
    {
        await using var member = new PageHarness(new Harness().Member());
        member.Show<MainMenuPage>();
        Assert.Equal(ScreenId.Logout, await member.RunAsync(ScreenId.MainMenu, "L"));
        Assert.Null(member.H.Host.Live);
        Assert.Empty(member.H.Navigator.Moves);

        var h = new Harness();
        h.Session.Me = h.Api.Me;
        await using var guest = new PageHarness(h);
        guest.Show<MainMenuPage>();
        Assert.Equal(ScreenId.SignIn, await guest.RunAsync(ScreenId.MainMenu, "d"));
    }
}

/// <summary>
/// Tests for the bulletin pages: the list, one bulletin, the SysOp cam and Top Players.
/// </summary>
public sealed class BulletinPageTests
{
    /// <summary>The list shows each bulletin, links to it, and mirrors its links for screen readers.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ListsTheBulletins()
    {
        await using var p = new PageHarness(path: "/bulletins");
        var cut = p.Show<BulletinsPage>();
        await p.RunAsync(ScreenId.Bulletins, "9", "1", "T", "q", DoorKeys.Escape);

        Assert.Equal([Transition.Wipe], p.H.Terminal.Transitions);
        Assert.Contains("SYSTEM BULLETINS", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Bulletin #1: Now", p.Plain, StringComparison.Ordinal);
        Assert.Contains("(updated today)", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/bulletins/1", false), ("/bulletins/top", false), ("/menu", false), ("/menu", false)], p.H.Navigator.Moves);
        Assert.Equal("[T] Top players", cut.Find("a[href='/bulletins/top']").TextContent);
        Assert.Contains("Bulletin #2: About", cut.Markup, StringComparison.Ordinal);
        p.AssertNoPlaceholders();
    }

    /// <summary>A short bulletin fits one page: it asks for a key straight away, and any key goes back to the list.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ReadsABulletin()
    {
        await using var p = new PageHarness(path: "/bulletins/1");
        p.Show<BulletinPage>(ps => ps.Add(x => x.Number, 1));
        await p.RunAsync(ScreenId.Bulletins, "x");

        Assert.Contains("BULLETIN #1: NOW", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Hello world", p.Plain, StringComparison.Ordinal);
        Assert.Contains("press any key", p.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("-- more --", p.Plain, StringComparison.Ordinal);
        Assert.Empty(p.H.Terminal.Animations);
        Assert.Equal([("/bulletins", false)], p.H.Navigator.Moves);
        p.AssertNoPlaceholders();
    }

    /// <summary>The SysOp cam shows a frame as the bulletin draws and plays once the last page shows.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PlaysTheSysopCam()
    {
        await using var p = new PageHarness(path: "/bulletins/2");
        p.Show<BulletinPage>(ps => ps.Add(x => x.Number, 2));
        await p.RunAsync(ScreenId.Bulletins, "x");

        Assert.Equal(2, p.H.Terminal.Animations.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1), p.H.Terminal.Animations[0].Options.MaxDuration);
        Assert.True(p.H.Terminal.Animations[1].Options.Loop);
        Assert.Equal(BulletinPage.CamBox, p.H.Terminal.Animations[1].Options);
        Assert.Contains("SysOp cam is live", p.Plain, StringComparison.Ordinal);
        Assert.Contains("press any key", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/bulletins", false)], p.H.Navigator.Moves);
    }

    /// <summary>A long bulletin pages: Enter (or any other key) turns the page, then the key prompt shows.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PagesLongBulletins()
    {
        var h = new Harness();
        var lines = string.Join(",", Enumerable.Range(1, 40).Select(i => $"\"line {i}\""));
        h.Http.With("content/bulletins.json", $$"""{ "bulletins": [ { "number": 3, "title": "Long", "lines": [{{lines}}] } ] }""");
        await using var p = new PageHarness(h, "/bulletins/3");
        p.Show<BulletinPage>(ps => ps.Add(x => x.Number, 3));
        await p.RunAsync(ScreenId.Bulletins, DoorKeys.Enter, "n");

        Assert.Contains("-- more --", p.Plain, StringComparison.Ordinal);
        Assert.Contains("line 40", p.Plain, StringComparison.Ordinal);
        Assert.Contains("press any key", p.Plain, StringComparison.Ordinal);
        Assert.Empty(p.H.Navigator.Moves);

        await p.RunAsync(ScreenId.Bulletins, "q");
        Assert.Equal([("/bulletins", false)], p.H.Navigator.Moves);
    }

    /// <summary>An unknown bulletin number shows "file not found"; any key goes to the main menu.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task UnknownBulletinsAreNotFound()
    {
        await using var p = new PageHarness(path: "/bulletins/99");
        p.Show<BulletinPage>(ps => ps.Add(x => x.Number, 99));
        await p.RunAsync(ScreenId.Bulletins, "x");
        Assert.Contains("FILE NOT FOUND ON THIS DRIVE", p.Plain, StringComparison.Ordinal);
        Assert.Equal([Transition.None], p.H.Terminal.Transitions);
        Assert.Equal([("/menu", false)], p.H.Navigator.Moves);
    }

    /// <summary>Top Players shows today's boards by the caller's date, and any key goes back once loaded.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ShowsTopPlayers()
    {
        var h = new Harness();
        h.Api.Scores = new(ApiStatus.Ok, [new ScoreEntry(1, "Neo", 1234)]);
        await using var p = new PageHarness(h, "/bulletins/top");
        var cut = p.Show<TopPlayersPage>();
        cut.WaitForAssertion(() => Assert.Contains("SEEKERS", cut.Markup, StringComparison.Ordinal));
        await p.RunAsync(ScreenId.Bulletins, "x");

        Assert.Contains("TOP PLAYERS", p.Plain, StringComparison.Ordinal);
        Assert.Contains("The Modem Oracle - SEEKERS", p.Plain, StringComparison.Ordinal);
        Assert.Contains("1,234", p.Plain, StringComparison.Ordinal);
        Assert.Contains("press any key", p.Plain, StringComparison.Ordinal);
        Assert.Equal(["scores:oracle:2026-11-15"], h.Api.Calls);
        Assert.Equal([("/bulletins", false)], p.H.Navigator.Moves);
    }

    /// <summary>Empty boards say "nobody yet", and no scored doors says so.</summary>
    [Fact]
    public void EmptyTopPlayers()
    {
        var door = new DoorManifestEntry { Id = "x", Title = "X", ScoreTitle = "WINS" };
        var empty = TopPlayersPage.Lines([(door, new ApiResult<IReadOnlyList<ScoreEntry>>(ApiStatus.Failed))]);
        Assert.Contains(empty, l => l.Contains("Nobody yet", StringComparison.Ordinal));
        Assert.Contains("No scored doors", Assert.Single(TopPlayersPage.Lines([])), StringComparison.Ordinal);
    }
}

/// <summary>
/// Tests for the message board page.
/// </summary>
public sealed class BoardPageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Threads put replies under their parents, oldest first, with depth capped at 4.</summary>
    [Fact]
    public void ThreadsMessages()
    {
        var messages = new List<MessageDto>
        {
            new(2, 1, "b", "reply", T0.AddMinutes(2)),
            new(1, null, "a", "root", T0),
            new(9, 99, "z", "orphan", T0.AddMinutes(1)),
            new(3, 2, "c", "r2", T0.AddMinutes(3)),
            new(4, 3, "d", "r3", T0.AddMinutes(4)),
            new(5, 4, "e", "r4", T0.AddMinutes(5)),
            new(6, 5, "f", "r5", T0.AddMinutes(6)),
        };
        var thread = BoardPage.Thread(messages);
        Assert.Equal([1L, 2, 3, 4, 5, 6, 9], thread.Select(t => t.Message.Id));
        Assert.Equal([0, 1, 2, 3, 4, 4, 0], thread.Select(t => t.Depth));
    }

    /// <summary>Messages are escaped so visitors can't inject color codes.</summary>
    [Fact]
    public void FormatsMessagesSafely()
    {
        var lines = BoardPage.FormatMessage(new MessageDto(7, null, "x|14y", "hi |09red", T0), 1);
        var plain = string.Join("\n", lines.Select(PipeCodes.StripToPlain));
        Assert.Contains("#7", plain, StringComparison.Ordinal);
        Assert.Contains("x|14y", plain, StringComparison.Ordinal);
        Assert.Contains("hi |09red", plain, StringComparison.Ordinal);
        Assert.Contains("└", plain, StringComparison.Ordinal);
    }

    /// <summary>A guest reads a blank wall; N and R go to the compose addresses and Q to the menu. Nothing is posted.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ReadsTheWall()
    {
        await using var p = new PageHarness(path: "/board");
        var cut = p.Show<BoardPage>();
        cut.WaitForAssertion(() => Assert.Contains("The wall is blank", cut.Markup, StringComparison.Ordinal));
        await p.RunAsync(ScreenId.MessageBoard, "z", "N", "r", "Q");

        Assert.Contains("MESSAGE BOARDS", p.Plain, StringComparison.Ordinal);
        Assert.Contains("The wall is blank", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/board/new", false), ("/board/reply", false), ("/menu", false)], p.H.Navigator.Moves);
        Assert.Empty(p.H.Api.Calls);
    }

    /// <summary>A long wall pages; a failed load shows line noise.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PagesAndFails()
    {
        var h = new Harness();
        h.Api.Messages = new(ApiStatus.Ok, Enumerable.Range(1, 12).Select(i => new MessageDto(i, null, "Neo", $"message {i}", T0.AddMinutes(i))).ToList());
        await using var p = new PageHarness(h, "/board");
        var cut = p.Show<BoardPage>();
        cut.WaitForAssertion(() => Assert.Contains("message 12", cut.Markup, StringComparison.Ordinal));
        await p.RunAsync(ScreenId.MessageBoard, DoorKeys.Enter, DoorKeys.Enter);
        Assert.Contains("-- more --", p.Plain, StringComparison.Ordinal);
        Assert.Contains("message 12", p.Plain, StringComparison.Ordinal);

        var failed = new Harness();
        failed.Api.Messages = new(ApiStatus.Failed);
        Assert.Contains("LINE NOISE", Assert.Single(BoardPage.Lines(failed.Api.Messages)), StringComparison.Ordinal);
    }
}

/// <summary>
/// Tests for the file area pages.
/// </summary>
public sealed class FilePageTests
{
    /// <summary>The areas are lettered and link to their own addresses.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ListsTheAreas()
    {
        await using var p = new PageHarness(path: "/files");
        p.Show<FilesPage>();
        await p.RunAsync(ScreenId.FileAreas, "Z", "a", "Q");
        Assert.Contains("FILE AREAS", p.Plain, StringComparison.Ordinal);
        Assert.Contains("(2 files)", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/files/tools", false), ("/menu", false)], p.H.Navigator.Moves);
        p.AssertNoPlaceholders();
    }

    /// <summary>Area names become address segments.</summary>
    /// <param name="name">The area name.</param>
    /// <param name="slug">The segment.</param>
    [Theory]
    [InlineData("TOOLS", "tools")]
    [InlineData("Dev Tools!", "dev-tools")]
    [InlineData("  C++ & co  ", "c-co")]
    public void SlugsAreaNames(string name, string slug) => Assert.Equal(slug, FilesPage.Slug(name));

    /// <summary>
    /// Inside an area, a number downloads a file (a good one opens in a new tab, a missing one says so) and the
    /// area draws again afterwards; Escape goes back to the areas.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DownloadsFiles()
    {
        await using var p = new PageHarness(path: "/files/tools");
        p.Show<FileAreaPage>(ps => ps.Add(x => x.Area, "TOOLS"));
        await p.RunAsync(ScreenId.FileAreas, "5", "1", "x", "2", "x", DoorKeys.Escape);

        Assert.Equal([new Uri("https://example.com/good")], p.H.Terminal.Opened);
        Assert.Contains("FILE AREA: TOOLS", p.Plain, StringComparison.Ordinal);
        Assert.Contains("GOOD.ZIP", p.Plain, StringComparison.Ordinal);
        Assert.Contains("TRY DRIVE B:", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Opening example.com", p.Plain, StringComparison.Ordinal);
        Assert.Equal([Transition.Wipe, Transition.Wipe, Transition.Wipe], p.H.Terminal.Transitions);
        Assert.Equal([("/files", false)], p.H.Navigator.Moves);
    }

    /// <summary>An unknown area shows "file not found".</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task UnknownAreasAreNotFound()
    {
        await using var p = new PageHarness(path: "/files/nope");
        p.Show<FileAreaPage>(ps => ps.Add(x => x.Area, "nope"));
        await p.RunAsync(ScreenId.FileAreas, "x");
        Assert.Contains("FILE NOT FOUND ON THIS DRIVE", p.Plain, StringComparison.Ordinal);
    }

    /// <summary>The progress bar fills proportionally and clamps.</summary>
    [Fact]
    public void DrawsProgress()
    {
        Assert.Equal(15, PipeCodes.StripAnsi(FileAreaPage.ProgressBar(50)).Count(c => c == '█'));
        Assert.Equal(0, PipeCodes.StripAnsi(FileAreaPage.ProgressBar(-5)).Count(c => c == '█'));
        Assert.Equal(30, PipeCodes.StripAnsi(FileAreaPage.ProgressBar(150)).Count(c => c == '█'));
    }
}

/// <summary>
/// Tests for the door list and user list pages.
/// </summary>
public sealed class DoorAndUserPageTests
{
    /// <summary>Guests see locks and are refused; open doors run, and the list draws again afterwards.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DoorsListRunsDoors()
    {
        var h = new Harness();
        h.Loader.Door = new LambdaDoor(c => c.WriteAsync("INSIDE THE DOOR"));
        await using var p = new PageHarness(h, "/doors");
        p.Show<DoorsPage>();
        await p.RunAsync(ScreenId.Doors, "2", "x", "1", "x", "7", "Q");

        Assert.Contains("DOOR GAMES", p.Plain, StringComparison.Ordinal);
        Assert.Contains("[LOCKED - LVL 10]", p.Plain, StringComparison.Ordinal);
        Assert.Contains("[SCORED]", p.Plain, StringComparison.Ordinal);
        Assert.Contains("ACCESS DENIED", p.Plain, StringComparison.Ordinal);
        Assert.Contains("INSIDE THE DOOR", p.Plain, StringComparison.Ordinal);
        Assert.Equal(3, p.H.Terminal.Transitions.Count(t => t == Transition.Wipe));
        Assert.Equal([("/menu", false)], p.H.Navigator.Moves);
        Assert.Contains("closed for repairs", Assert.Single(DoorsPage.Lines([], h.Session)), StringComparison.Ordinal);

        h.Member();
        Assert.DoesNotContain(DoorsPage.Lines((await h.Content.GetDoorsAsync()).EnabledDoors.ToList(), h.Session), l => l.Contains("LOCKED", StringComparison.Ordinal));
    }

    /// <summary>The user list shows the hall of fame, recent callers and the font credits, then goes back.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task UserListShowsFameAndCallers()
    {
        var h = new Harness();
        h.Api.Callers = new(ApiStatus.Ok, [new CallerDto("Neo", DateTimeOffset.UnixEpoch, 10)]);
        await using var p = new PageHarness(h, "/users");
        var cut = p.Show<UsersPage>();
        cut.WaitForAssertion(() => Assert.Contains("Legend", cut.Markup, StringComparison.Ordinal));
        await p.RunAsync(ScreenId.UserList, "x");

        Assert.Contains("Legend", p.Plain, StringComparison.Ordinal);
        Assert.Contains("LVL 99", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Neo", p.Plain, StringComparison.Ordinal);
        Assert.Contains("https://example.com/legend", p.H.Terminal.Output, StringComparison.Ordinal);
        Assert.Contains("The Ultimate Oldschool PC Font Pack", p.Plain, StringComparison.Ordinal);
        Assert.Contains("CC BY-SA 4.0", p.Plain, StringComparison.Ordinal);
        Assert.Contains("https://int10h.org/oldschool-pc-fonts/", p.H.Terminal.Output, StringComparison.Ordinal);
        Assert.Contains("https://creativecommons.org/licenses/by-sa/4.0/", p.H.Terminal.Output, StringComparison.Ordinal);
        Assert.Equal([("/menu", false)], p.H.Navigator.Moves);
    }

    /// <summary>A failed caller list says so, and an empty one says nobody signed.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task UserListFailure()
    {
        var h = new Harness();
        h.Api.Callers = new(ApiStatus.Failed);
        await using var p = new PageHarness(h, "/users");
        var cut = p.Show<UsersPage>();
        cut.WaitForAssertion(() => Assert.Contains("LINE NOISE", cut.Markup, StringComparison.Ordinal));
        await p.RunAsync(ScreenId.UserList, "x");
        Assert.Contains("LINE NOISE", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Nobody has signed the log yet", p.Plain, StringComparison.Ordinal);
    }
}

/// <summary>
/// Tests for the page model's rules: access, titles, no actions on load, and the host's edges.
/// </summary>
public sealed class PageModelTests
{
    /// <summary>Every screen page says who may open it, and that agrees with its route.</summary>
    [Fact]
    public void PagesDeclareAccess()
    {
        var pages = typeof(BbsPage).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(BbsPage))).ToList();
        Assert.Equal(9, pages.Count);
        Assert.All(pages, page =>
        {
            var access = page.GetCustomAttributes(typeof(BbsAccessAttribute), inherit: false).Cast<BbsAccessAttribute>().Single();
            foreach (var route in page.GetCustomAttributes(typeof(RouteAttribute), inherit: false).Cast<RouteAttribute>())
            {
                Assert.True(RouteTable.TryResolve(Regex.Replace(route.Template, "{[^}]*}", "x"), out var r), route.Template);
                Assert.Equal(r.Access, access.Access);
            }
        });
    }

    /// <summary>Every page binds every text slot of its art.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PagesBindEverySlot()
    {
        var h = new Harness();
        await using var p = new PageHarness(h);
        Check(p.Show<MainMenuPage>());
        Check(p.Show<BulletinsPage>());
        Check(p.Show<BulletinPage>(ps => ps.Add(x => x.Number, 1)));
        Check(p.Show<BulletinPage>(ps => ps.Add(x => x.Number, 2)));
        Check(p.Show<TopPlayersPage>());
        Check(p.Show<BoardPage>());
        Check(p.Show<FilesPage>());
        Check(p.Show<FileAreaPage>(ps => ps.Add(x => x.Area, "tools")));
        Check(p.Show<DoorsPage>());
        Check(p.Show<UsersPage>());

        static void Check<T>(IRenderedComponent<T> page)
            where T : class, IComponent => page.WaitForAssertion(() => PageHarness.AssertAllSlotsBound(page));
    }

    /// <summary>Opening a page never calls the server to change anything (only reads, such as score boards).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NoPageActsOnLoad()
    {
        var h = new Harness().Member(sysop: true);
        await using var p = new PageHarness(h);
        p.Show<MainMenuPage>();
        p.Show<BulletinsPage>();
        p.Show<BulletinPage>(ps => ps.Add(x => x.Number, 2));
        p.Show<BoardPage>();
        p.Show<FilesPage>();
        p.Show<FileAreaPage>(ps => ps.Add(x => x.Area, "tools"));
        p.Show<DoorsPage>();
        p.Show<UsersPage>();
        var top = p.Show<TopPlayersPage>();
        top.WaitForAssertion(() => Assert.NotEmpty(h.Api.Calls));
        Assert.All(h.Api.Calls, c => Assert.StartsWith("scores:", c, StringComparison.Ordinal));
        Assert.Empty(h.Terminal.Output);
    }

    /// <summary>The host needs a terminal before it can draw, and stays quiet without one.</summary>
    [Fact]
    public void HostWaitsForTheTerminal()
    {
        var host = new BbsHost(new FakeNavigator());
        Assert.False(host.Ready.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => host.Context);
        host.Write("ignored");
        Assert.Null(host.Live);
        Assert.Equal(CancellationToken.None, host.PageToken);

        var h = new Harness();
        Assert.True(h.Host.Ready.IsCompleted);
        Assert.Same(h.Terminal, h.Host.Terminal);
        Assert.Equal("/", h.Host.CurrentPath);
    }

    /// <summary>Fields turn values into text: strings as they are, numbers in the invariant culture, null as nothing.</summary>
    [Fact]
    public void FieldsFormatValues()
    {
        Assert.Equal(string.Empty, Field.Text(null));
        Assert.Equal("|14hi", Field.Text("|14hi"));
        Assert.Equal("1234.5", Field.Text(1234.5));
        Assert.Equal("Wipe", Field.Text(Transition.Wipe));
        Assert.Equal("x", ScreenMirror.Plain("|14x|07 "));
    }

    /// <summary>Components outside a screen are a mistake the page author hears about at once.</summary>
    [Fact]
    public void ComponentsNeedAScreen()
    {
        using var bunit = new BunitContext();
        Assert.Throws<InvalidOperationException>(() => bunit.Render<Field>(ps => ps.Add(x => x.Slot, Screens.Art.MainMenuArt.Handle)));
        Assert.Throws<InvalidOperationException>(() => bunit.Render<Hotkey>(ps => ps.Add(x => x.Key, "Q")));
        Assert.Throws<InvalidOperationException>(() => bunit.Render<List>(ps => ps.Add(x => x.Slot, Screens.Art.UsersArt.Body)));
    }
}
