using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens;
using Pizon.Web.Client.Screens.Art;
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
    /// Runs the host's pump for the current address's route until the scripted keys run out or the address
    /// moves to another route.
    /// </summary>
    /// <param name="keys">The keys to press.</param>
    /// <returns>The path the address moved to, or <see langword="null"/> when the keys ran out.</returns>
    public async Task<string?> RunAsync(params string[] keys)
    {
        Assert.True(RouteTable.TryResolve(H.Host.CurrentPath, out var route));
        H.Terminal.Keys(keys);
        using var stop = new CancellationTokenSource();
        H.Terminal.StopWhenEmpty = stop;
        try
        {
            await Bunit.Renderer.Dispatcher.InvokeAsync(() => H.Host.RunPagesAsync(route.Path, stop.Token));
            return H.Host.CurrentPath;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs the whole call from the current address (pages already shown with <see cref="Show"/> draw when
    /// their address comes up) until the scripted keys run out or the test stops it.
    /// </summary>
    /// <param name="stop">Stops the call (also cancelled when the keys run out).</param>
    /// <returns>A task that completes when the call ends.</returns>
    public async Task RunAppAsync(CancellationTokenSource stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        H.Terminal.StopWhenEmpty = stop;
        await Bunit.Renderer.Dispatcher.InvokeAsync(() => H.Host.RunAsync(stop.Token));
    }

    /// <summary>
    /// Every slot in the page's art is bound by the page: fields, inputs and animations (the cursor slot is
    /// where the prompt goes).
    /// </summary>
    /// <param name="page">The rendered page.</param>
    /// <typeparam name="T">The page.</typeparam>
    public static void AssertAllSlotsBound<T>(IRenderedComponent<T> page)
        where T : class, IComponent
    {
        var screen = page.FindComponent<BbsScreen>().Instance;
        var expected = screen.Art.Fields.Where(f => f.Name != ScreenArt.CursorName).Select(f => f.Name).Order(StringComparer.Ordinal);
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
        Assert.Equal("/bulletins", await p.RunAsync(DoorKeys.Escape, "z", "1"));

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
        await sysop.RunAsync();
        Assert.Contains("Change Handle", sysop.Plain, StringComparison.Ordinal);
        Assert.Contains("Log out of Discord", sysop.Plain, StringComparison.Ordinal);
        Assert.Contains("SysOp Console", sysop.Plain, StringComparison.Ordinal);
        Assert.Contains("SysOp1", sysop.Plain, StringComparison.Ordinal);
        Assert.Equal("[*] SysOp Console", page.Find("a[href='/sysop']").TextContent);
    }

    /// <summary>
    /// The menu's address hotkeys go exactly where <see cref="RipPage.Route"/> sends the RIP card's buttons,
    /// for every kind of caller.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task HotkeysAgreeWithTheRipCard()
    {
        var guest = new Harness();
        guest.Session.Me = guest.Api.Me;
        foreach (var h in new[] { guest, new Harness(), new Harness().Member(), new Harness().Member(sysop: true) })
        {
            await using var p = new PageHarness(h);
            var screen = p.Show<MainMenuPage>().FindComponent<BbsScreen>().Instance;
            foreach (var key in "12345phsg*zq".Select(c => c.ToString()).Append(DoorKeys.Enter))
            {
                Assert.Equal(RipPage.Route(key, h.Session), screen.Hotkeys.FirstOrDefault(k => k.Matches(key))?.Href);
            }
        }
    }

    /// <summary>
    /// [L] logs out of Discord (reloading the BBS), and a refused logout says why and redraws the menu; [D]
    /// leaves for Discord sign-in.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SignsInAndOut()
    {
        await using var member = new PageHarness(new Harness().Member());
        member.Show<MainMenuPage>();
        Assert.Null(await member.RunAsync("L"));
        Assert.Equal(["/"], member.H.Navigator.Navigations);
        Assert.Equal(1, member.H.Presence.Disconnects);
        Assert.Contains("Shredding your sign-in cookie", member.Plain, StringComparison.Ordinal);

        var refused = new Harness().Member();
        refused.Api.WriteResult = new ApiResult(ApiStatus.Failed);
        await using var again = new PageHarness(refused);
        again.Show<MainMenuPage>();
        Assert.Null(await again.RunAsync("L", "x"));
        Assert.Contains("LINE NOISE", again.Plain, StringComparison.Ordinal);
        Assert.Equal(2, refused.Terminal.Transitions.Count);
        Assert.Empty(refused.Navigator.Navigations);

        var h = new Harness();
        h.Session.Me = h.Api.Me;
        await using var guest = new PageHarness(h);
        guest.Show<MainMenuPage>();
        Assert.Null(await guest.RunAsync("d"));
        Assert.Equal([BbsHost.SignInUrl(dev: false)], h.Navigator.Navigations);
        Assert.Contains("Discord switchboard", guest.Plain, StringComparison.Ordinal);
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
        Assert.Equal("/menu", await p.RunAsync("9", "A", "T", "q", DoorKeys.Escape));

        Assert.Equal([Transition.Wipe], p.H.Terminal.Transitions);
        Assert.Contains("SYSTEM BULLETINS", p.Plain, StringComparison.Ordinal);
        Assert.Contains("[A] Bulletin #1: Now", p.Plain, StringComparison.Ordinal);
        Assert.Contains("(updated today)", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/bulletins/1", false), ("/bulletins/top", false), ("/menu", false)], p.H.Navigator.Moves);
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
        await p.RunAsync("x");

        Assert.Contains("BULLETIN #1: NOW", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Hello world", p.Plain, StringComparison.Ordinal);
        Assert.Contains("press any key", p.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("-- more --", p.Plain, StringComparison.Ordinal);
        Assert.Empty(p.H.Terminal.Animations);
        Assert.Equal([("/bulletins", false)], p.H.Navigator.Moves);
        p.AssertNoPlaceholders();
    }

    /// <summary>
    /// The SysOp cam plays in its slot as soon as the bulletin is drawn (idle frames while the caller is alone),
    /// switches to its busy frames when someone else calls, and never takes keys.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PlaysTheSysopCam()
    {
        await using var p = new PageHarness(path: "/bulletins/2");
        p.Show<BulletinPage>(ps => ps.Add(x => x.Number, 2));
        p.H.Terminal.OnRead = k =>
        {
            if (k == "x")
            {
                p.H.Presence.Set(1, 2);
            }
        };
        await p.RunAsync("x");

        Assert.Empty(p.H.Terminal.Animations);
        Assert.Equal(2, p.H.Terminal.SlotsStarted.Count);
        var (name, idle) = p.H.Terminal.SlotsStarted[0];
        Assert.Equal("CAM", name);
        Assert.Equal((BulletinCamArt.Cam.Row, BulletinCamArt.Cam.Column, 24, 12), (idle.Row, idle.Column, idle.Width, idle.Height));
        Assert.Equal(SysopCamArt.Instance.FrameAnsi(0), idle.Frames[0]);
        Assert.Equal(8, idle.Frames.Count);
        Assert.All(idle.Delays, d => Assert.Equal(125, d));
        Assert.True(idle.Loop);
        Assert.Null(idle.MaxDuration);
        Assert.Equal(SysopCamArt.Instance.FrameAnsi(8), p.H.Terminal.SlotsStarted[1].Animation.Frames[0]);
        Assert.Equal(SysopCamArt.Ranges.Busy, BulletinPage.CamRange(2));
        Assert.Equal(SysopCamArt.Ranges.Idle, BulletinPage.CamRange(1));
        Assert.Contains("press any key", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/bulletins", false)], p.H.Navigator.Moves);
        p.AssertNoPlaceholders();
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
        await p.RunAsync(DoorKeys.Enter, "n");

        Assert.Contains("-- more --", p.Plain, StringComparison.Ordinal);
        Assert.Contains("line 40", p.Plain, StringComparison.Ordinal);
        Assert.Contains("press any key", p.Plain, StringComparison.Ordinal);
        Assert.Empty(p.H.Navigator.Moves);

        await p.RunAsync("q");
        Assert.Equal([("/bulletins", false)], p.H.Navigator.Moves);
    }

    /// <summary>An unknown bulletin number shows "file not found"; any key goes to the main menu.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task UnknownBulletinsAreNotFound()
    {
        await using var p = new PageHarness(path: "/bulletins/99");
        p.Show<BulletinPage>(ps => ps.Add(x => x.Number, 99));
        await p.RunAsync("x");
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
        await p.RunAsync("x");

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
        p.H.Navigator.OnNavigate = _ => p.H.Navigator.CurrentUri = new Uri("http://localhost/board");
        await p.RunAsync("z", "N", "r", "Q");

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
        await p.RunAsync(DoorKeys.Enter, DoorKeys.Enter);
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
        await p.RunAsync("Z", "a", "Q");
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
        await p.RunAsync("5", "1", "x", "2", "x", DoorKeys.Escape);

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
        await p.RunAsync("x");
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
        await p.RunAsync("2", "x", "1", "x", "7", "Q");

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
        await p.RunAsync("x");

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
        await p.RunAsync("x");
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
        var pages = typeof(BbsPage).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(BbsPage)) && !t.IsAbstract).ToList();
        Assert.Equal(24, pages.Count);
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

    /// <summary>Every page binds every text slot of its art (shown to the SysOp, who may open them all).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PagesBindEverySlot()
    {
        var h = new Harness().Member(sysop: true);
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
        Check(p.Show<SettingsPage>());
        Check(p.Show<HandlePage>());
        Check(p.Show<NewUserHandlePage>());
        Check(p.Show<PageSysopPage>());
        Check(p.Show<BoardNewPage>());
        Check(p.Show<BoardReplyPage>());
        Check(p.Show<SysopPage>());
        Check(p.Show<SysopQueuePage>());
        Check(p.Show<SysopPagesPage>());
        Check(p.Show<SysopDeletePage>());

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
        p.Show<DiscordBulletinPage>(ps => ps.Add(x => x.Number, 1));
        p.Show<BoardPage>();
        p.Show<FilesPage>();
        p.Show<FileAreaPage>(ps => ps.Add(x => x.Area, "tools"));
        p.Show<DoorsPage>();
        p.Show<UsersPage>();
        p.Show<SettingsPage>();
        p.Show<HandlePage>();
        p.Show<NewUserHandlePage>();
        p.Show<PageSysopPage>();
        p.Show<BoardNewPage>();
        p.Show<BoardReplyPage>();
        p.Show<SysopPage>();
        p.Show<SysopQueuePage>();
        p.Show<SysopPagesPage>();
        p.Show<SysopDeletePage>();
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

/// <summary>
/// Tests for Discord bulletins: listed first on number keys, opened by their fixed number, and the offline line.
/// </summary>
public sealed class DiscordBulletinPageTests
{
    private static readonly DateTimeOffset Posted = new(2026, 11, 20, 18, 0, 0, TimeSpan.Zero);

    /// <summary>Discord posts come first on 1, 2..., the offline line follows them, then the lettered file bulletins.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ListsDiscordPostsFirst()
    {
        var h = new Harness();
        h.Api.Feed = new(ApiStatus.Ok, new FeedDto(true, false, [Post(12, "Server move"), Post(11, "Hello || pipes")]));
        await using var p = new PageHarness(h, "/bulletins");
        var cut = p.Show<BulletinsPage>();
        Assert.Equal("/menu", await p.RunAsync("1", "q"));

        Assert.Contains("[1] Bulletin #D12: Server move", p.Plain, StringComparison.Ordinal);
        Assert.Contains("[2] Bulletin #D11: Hello | pipes", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Discord relay offline", p.Plain, StringComparison.Ordinal);
        Assert.Contains("[A] Bulletin #1: Now", p.Plain, StringComparison.Ordinal);
        Assert.True(p.Plain.IndexOf("#D11", StringComparison.Ordinal) < p.Plain.IndexOf("relay offline", StringComparison.Ordinal));
        Assert.True(p.Plain.IndexOf("relay offline", StringComparison.Ordinal) < p.Plain.IndexOf("[A]", StringComparison.Ordinal));
        Assert.Contains("Bulletin #D11: Hello | pipes", cut.Find("a[href='/bulletins/d/11']").TextContent, StringComparison.Ordinal);
        Assert.Equal([("/bulletins/d/12", false), ("/menu", false)], p.H.Navigator.Moves);
    }

    /// <summary>Only as many Discord posts as fit are listed, never more than ten, and the offline line needs a configured feed.</summary>
    [Fact]
    public void FitsTheScreen()
    {
        var board = new Content.BulletinBoard { Bulletins = [new() { Number = 1, Title = "One" }, new() { Number = 2, Title = "Two" }] };
        var posts = Enumerable.Range(1, 12).Select(n => Post(n, "P" + n)).Reverse().ToList();

        var (entries, lines) = BulletinsPage.Build(board, new FeedDto(true, true, posts), 12);
        Assert.Equal(12, lines.Count);
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "A", "B"], entries.Select(e => e.Key));
        Assert.Equal("/bulletins/d/12", entries[0].Href);

        var (offlineEntries, offlineLines) = BulletinsPage.Build(board, new FeedDto(true, false, posts), 5);
        Assert.Equal(2, offlineEntries.Count(e => e.Href.StartsWith("/bulletins/d/", StringComparison.Ordinal)));
        Assert.Equal(BulletinsPage.OfflineLine, offlineLines[2]);

        var (_, disabled) = BulletinsPage.Build(board, new FeedDto(false, false, []), 12);
        Assert.Equal(2, disabled.Count);
        var (_, tiny) = BulletinsPage.Build(board, new FeedDto(true, true, posts), 1);
        Assert.Equal(2, tiny.Count);
    }

    /// <summary>File bulletin keys run A, B, C... and skip Q and T, which the screen already uses.</summary>
    [Fact]
    public void FileKeysSkipTakenLetters()
    {
        Assert.Equal("A", BulletinsPage.FileKey(0));
        Assert.Equal("P", BulletinsPage.FileKey(15));
        Assert.Equal("R", BulletinsPage.FileKey(16));
        Assert.Equal("U", BulletinsPage.FileKey(18));
        Assert.Equal("Z", BulletinsPage.FileKey(23));
        Assert.Null(BulletinsPage.FileKey(24));
        Assert.Throws<ArgumentOutOfRangeException>(() => BulletinsPage.FileKey(-1));
    }

    /// <summary>A Discord bulletin shows its fixed number, who posted it and when, then the message.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ReadsADiscordBulletin()
    {
        var h = new Harness();
        h.Api.FeedPosts.Add(Post(12, "Server move") with { EditedAt = Posted.AddHours(1) });
        await using var p = new PageHarness(h, "/bulletins/d/12");
        p.Show<DiscordBulletinPage>(ps => ps.Add(x => x.Number, 12));
        await p.RunAsync("x");

        Assert.Contains("BULLETIN #D12: SERVER MOVE", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Posted by David", p.Plain, StringComparison.Ordinal);
        Assert.Contains("edited", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Body of 12", p.Plain, StringComparison.Ordinal);
        Assert.Contains("press any key", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/bulletins", false)], p.H.Navigator.Moves);
        p.AssertNoPlaceholders();
    }

    /// <summary>A post never edited has no "edited" note; long lines wrap to the body width.</summary>
    [Fact]
    public void BuildsLines()
    {
        var lines = DiscordBulletinPage.Lines(Post(3, "T") with { Lines = [new string('x', 100)] }, 76);
        Assert.DoesNotContain("edited", lines[0], StringComparison.Ordinal);
        Assert.Equal(string.Empty, lines[1]);
        Assert.Equal(4, lines.Count);
    }

    /// <summary>An unknown or deleted Discord bulletin shows "file not found".</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task UnknownPostsAreNotFound()
    {
        await using var p = new PageHarness(path: "/bulletins/d/99");
        p.Show<DiscordBulletinPage>(ps => ps.Add(x => x.Number, 99));
        await p.RunAsync("x");
        Assert.Contains("FILE NOT FOUND ON THIS DRIVE", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/menu", false)], p.H.Navigator.Moves);
    }

    private static FeedPostDto Post(int number, string title) =>
        new(number, title, [$"Body of {number}"], "David", Posted, null);
}
