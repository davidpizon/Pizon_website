using System.Net;
using Pizon.Doors.Sdk;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Doors;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// Tests for line input.
/// </summary>
public sealed class LineEditorTests
{
    /// <summary>Editing keys work, non-printable keys are ignored and the length is capped.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task EditsALine()
    {
        var t = new FakeTerminal().Keys(DoorKeys.Backspace, DoorKeys.Up, "a", "b", "c", "d", DoorKeys.Backspace, "e", DoorKeys.Enter);
        Assert.Equal("abe", await LineEditor.ReadLineAsync(t, 3));
        Assert.Equal([true, false], t.LineModes);

        var pre = new FakeTerminal().Keys(DoorKeys.Enter);
        Assert.Equal("abc", await LineEditor.ReadLineAsync(pre, 3, "abcdef"));
        Assert.StartsWith("abc", pre.Plain, StringComparison.Ordinal);

        Assert.Null(await LineEditor.ReadLineAsync(new FakeTerminal().Keys(DoorKeys.Escape), 3));
    }

    /// <summary>Messages end at a blank line, the line limit or the length limit; ESC cancels.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ReadsMessages()
    {
        var blank = new FakeTerminal().Type("one").Type("two").Keys(DoorKeys.Enter);
        Assert.Equal("one\ntwo", await LineEditor.ReadMessageAsync(blank, 5, 10, 100));

        var lines = new FakeTerminal().Type("a").Type("b");
        Assert.Equal("a\nb", await LineEditor.ReadMessageAsync(lines, 2, 10, 100));

        var full = new FakeTerminal().Type("abcdefgh");
        Assert.Equal("abcde", await LineEditor.ReadMessageAsync(full, 5, 10, 5));

        Assert.Null(await LineEditor.ReadMessageAsync(new FakeTerminal().Keys(DoorKeys.Escape), 5, 10, 100));
    }
}

/// <summary>
/// Tests for terminal settings.
/// </summary>
public sealed class TerminalSettingsTests
{
    /// <summary>Missing, broken or out-of-range stored settings fall back safely.</summary>
    /// <param name="stored">The stored JSON.</param>
    /// <param name="baud">The expected baud.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(null, 9600)]
    [InlineData("{ broken", 9600)]
    [InlineData("null", 9600)]
    [InlineData("""{"Baud":1200}""", 9600)]
    [InlineData("""{"Baud":2400,"Sound":true}""", 2400)]
    public async Task LoadsSafely(string? stored, int baud)
    {
        var t = new FakeTerminal();
        if (stored is not null)
        {
            t.Stored[TerminalSettings.StorageKey] = stored;
        }

        Assert.Equal(baud, (await TerminalSettings.LoadAsync(t)).Baud);
    }

    /// <summary>Baud cycles INSTANT, 2400, 9600 and labels itself.</summary>
    [Fact]
    public void CyclesBaud()
    {
        var s = new TerminalSettings();
        Assert.Equal("9600 BAUD", s.BaudLabel);
        Assert.Equal("INSTANT", s.NextBaud().BaudLabel);
        Assert.Equal(2400, s.NextBaud().NextBaud().Baud);
        Assert.Equal(0, (s with { Baud = 5 }).NextBaud().Baud);
    }
}

/// <summary>
/// Tests for the shared input limits.
/// </summary>
public sealed class LimitsTests
{
    /// <summary>Handle shapes and suggestions follow the rules.</summary>
    [Fact]
    public void ChecksHandles()
    {
        Assert.True(Limits.IsValidHandleShape("Neo_1-x"));
        Assert.False(Limits.IsValidHandleShape("ab"));
        Assert.False(Limits.IsValidHandleShape("bad handle"));
        Assert.False(Limits.IsValidHandleShape(null));
        Assert.Null(Limits.SuggestHandle(null));
        Assert.Null(Limits.SuggestHandle("  "));
        Assert.Null(Limits.SuggestHandle("!!"));
        Assert.Equal("Neo_The_One", Limits.SuggestHandle("Neo The One!"));
        Assert.Equal("abcdefghijklmnop", Limits.SuggestHandle("abcdefghijklmnopqrstuvwxyz"));
        Assert.Equal(0, MeResponse.Guest.Level);
    }
}

/// <summary>
/// Tests for the content store.
/// </summary>
public sealed class ContentStoreTests
{
    /// <summary>Content loads once and is cached.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LoadsAndCaches()
    {
        var h = new Harness();
        var first = await h.Content.GetBulletinsAsync();
        Assert.Same(first, await h.Content.GetBulletinsAsync());
        Assert.Equal(2, first.Bulletins.Count);
        Assert.Single((await h.Content.GetFilesAsync()).Areas);
        Assert.Equal(2, (await h.Content.GetHallOfFameAsync()).Entries.Count);
        var doors = await h.Content.GetDoorsAsync();
        Assert.Same(doors, await h.Content.GetDoorsAsync());
        Assert.Equal(4, h.Http.Requests.Count);
    }

    /// <summary>Missing or broken files give empty content.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task FallsBackToEmpty()
    {
        var http = new FakeHttp().With("content/files.json", "{ broken").With("content/doors.json", """{ "doors": [ { "id": "x" } ] }""");
        var store = new ContentStore(http.Client());
        Assert.Empty((await store.GetBulletinsAsync()).Bulletins);
        Assert.Empty((await store.GetFilesAsync()).Areas);
        Assert.Empty((await store.GetDoorsAsync()).Doors);

        var missing = new ContentStore(new FakeHttp().Client());
        Assert.Empty((await missing.GetDoorsAsync()).Doors);
        Assert.Throws<ArgumentNullException>(() => new ContentStore(null!));
    }

    /// <summary>Text files load, or are null when missing or unreachable.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LoadsText()
    {
        var http = new FakeHttp().With("a.txt", "hello");
        var store = new ContentStore(http.Client());
        Assert.Equal("hello", await store.GetTextAsync("a.txt"));
        Assert.Null(await store.GetTextAsync("b.txt"));
        http.Throw = new HttpRequestException("down");
        Assert.Null(await store.GetTextAsync("a.txt"));
        Assert.Empty((await new ContentStore(http.Client()).GetHallOfFameAsync()).Entries);
    }
}

/// <summary>
/// Tests for the HTTP API client.
/// </summary>
public sealed class BbsApiTests
{
    /// <summary>Status codes map to API statuses.</summary>
    /// <param name="code">The HTTP status.</param>
    /// <param name="expected">The API status.</param>
    [Theory]
    [InlineData(HttpStatusCode.OK, ApiStatus.Ok)]
    [InlineData(HttpStatusCode.Accepted, ApiStatus.Ok)]
    [InlineData(HttpStatusCode.Unauthorized, ApiStatus.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ApiStatus.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ApiStatus.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiStatus.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, ApiStatus.Invalid)]
    [InlineData(HttpStatusCode.Conflict, ApiStatus.Invalid)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ApiStatus.Invalid)]
    [InlineData(HttpStatusCode.InternalServerError, ApiStatus.Failed)]
    public void MapsStatus(HttpStatusCode code, ApiStatus expected) => Assert.Equal(expected, BbsApi.Map(code));

    /// <summary>Reads parse JSON; failures keep an empty list and the error text.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ReadsData()
    {
        var http = new FakeHttp()
            .With("api/me", """{"authenticated":true,"handle":"Neo","suggestedHandle":"Neo","level":10,"isSysop":false,"discordEnabled":true,"devLoginEnabled":false}""")
            .With("api/messages", """[{"id":1,"parentId":null,"handle":"Neo","body":"hi","postedAt":"2026-11-15T00:00:00Z"}]""")
            .With("api/callers/recent", "not json")
            .With("api/scores/the%20oracle?day=2026-11-15", """[{"rank":1,"handle":"Neo","score":5}]""")
            .With("api/sysop/queue", "\"nope\"", HttpStatusCode.Unauthorized)
            .With("api/sysop/pages", "  ", HttpStatusCode.Forbidden);
        var api = new BbsApi(http.Client());

        Assert.Equal("Neo", (await api.GetMeAsync()).Handle);
        Assert.Equal("hi", Assert.Single((await api.GetMessagesAsync()).Value!).Body);
        var callers = await api.GetRecentCallersAsync();
        Assert.Equal(ApiStatus.Failed, callers.Status);
        Assert.Empty(callers.Value!);
        Assert.Equal(5, Assert.Single((await api.GetScoresAsync("the oracle", "2026-11-15")).Value!).Score);
        var queue = await api.GetModerationQueueAsync();
        Assert.Equal(ApiStatus.Unauthorized, queue.Status);
        Assert.Equal("nope", queue.Error);
        var pages = await api.GetPagesAsync();
        Assert.Equal(ApiStatus.Forbidden, pages.Status);
        Assert.Null(pages.Error);
        Assert.All(http.Requests, r => Assert.True(r.Headers.Contains(BbsApi.CsrfHeader)));
    }

    /// <summary>Feeds read the newest posts and single posts by number; a missing post is Forbidden with no value.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ReadsFeeds()
    {
        var http = new FakeHttp()
            .With("api/feeds/bulletins", """{"enabled":true,"online":false,"posts":[{"number":3,"title":"Hi","lines":["a"],"author":"D","postedAt":"2026-11-15T00:00:00Z","editedAt":null}]}""")
            .With("api/feeds/bulletins/3", """{"number":3,"title":"Hi","lines":["a"],"author":"D","postedAt":"2026-11-15T00:00:00Z","editedAt":null}""");
        var api = new BbsApi(http.Client());

        var feed = (await api.GetFeedAsync(Feeds.Bulletins)).Value!;
        Assert.True(feed.Enabled);
        Assert.False(feed.Online);
        Assert.Equal("Hi", Assert.Single(feed.Posts).Title);
        Assert.Equal(3, (await api.GetFeedPostAsync(Feeds.Bulletins, 3)).Value!.Number);
        var missing = await api.GetFeedPostAsync(Feeds.Bulletins, 4);
        Assert.Equal(ApiStatus.Forbidden, missing.Status);
        Assert.Null(missing.Value);
        Assert.False(FeedDto.None.Enabled);
    }

    /// <summary>Without a server, /api/me falls back to a guest.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task MeFallsBackToGuest()
    {
        var http = new FakeHttp { Throw = new HttpRequestException("down") };
        Assert.Same(MeResponse.Guest, await new BbsApi(http.Client()).GetMeAsync());
    }

    /// <summary>Writes go to the right place with the right method and body.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SendsWrites()
    {
        var http = new FakeHttp()
            .With("api/me/handle", string.Empty)
            .With("auth/logout", string.Empty)
            .With("api/messages", string.Empty, HttpStatusCode.Accepted)
            .With("api/page-sysop", string.Empty, HttpStatusCode.Accepted)
            .With("api/scores/oracle", string.Empty)
            .With("api/sysop/messages/5/moderate", string.Empty)
            .With("api/sysop/messages/5", string.Empty)
            .With("api/sysop/pages/6/read", string.Empty)
            .With("api/sysop/users/7/ban", "\"cannot ban yourself\"", HttpStatusCode.BadRequest);
        using var client = http.Client();
        _ = new BbsApi(client);
        var api = new BbsApi(client);

        Assert.True((await api.SetHandleAsync("Neo")).IsOk);
        Assert.True((await api.LogoutAsync()).IsOk);
        Assert.True((await api.PostMessageAsync(new PostMessageRequest(1, "hi"))).IsOk);
        Assert.True((await api.PageSysopAsync(new PageSysopRequest("yo"))).IsOk);
        Assert.True((await api.SubmitScoreAsync("oracle", new ScoreSubmitRequest(1, "2026-11-15"))).IsOk);
        Assert.True((await api.ModerateAsync(5, approve: true)).IsOk);
        Assert.True((await api.DeleteMessageAsync(5)).IsOk);
        Assert.True((await api.MarkPageReadAsync(6)).IsOk);
        var ban = await api.BanUserAsync(7);
        Assert.Equal(ApiStatus.Invalid, ban.Status);
        Assert.Equal("cannot ban yourself", ban.Error);

        Assert.Equal(HttpMethod.Delete, http.Requests[6].Method);
        Assert.Contains("\"handle\":\"Neo\"", http.Bodies[0], StringComparison.Ordinal);
        Assert.Contains("\"approve\":true", http.Bodies.Last(b => b.Contains("approve", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Single(client.DefaultRequestHeaders.GetValues(BbsApi.CsrfHeader));

        http.Throw = new HttpRequestException("down");
        Assert.Equal(ApiStatus.Failed, (await api.LogoutAsync()).Status);
    }
}

/// <summary>
/// Tests for the door host and what doors see.
/// </summary>
public sealed class DoorHostTests
{
    /// <summary>A member's door gets the intro, title card and a full working context.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RunsADoorWithItsContext()
    {
        var h = new Harness().Member();
        h.Http.With("_content/Pizon.Doors.Oracle/data.json", "{}");
        h.Api.Scores = new(ApiStatus.Ok, [new ScoreEntry(1, "Neo", 3)]);
        var results = new List<object?>();
        var door = new LambdaDoor(async c =>
        {
            await c.WriteAsync("w");
            await c.ClearAsync();
            results.Add(await c.ReadKeyAsync());
            results.Add(await c.ReadLineAsync(10));
            await c.DelayAsync(TimeSpan.FromSeconds(1));
            results.Add(await c.PlayAnimationAsync("a.json"));
            results.Add(await c.PlayAnimationAsync("../x.json"));
            results.Add(await c.LoadTextAsync("data.json"));
            results.Add(await c.LoadTextAsync("C:/x"));
            results.Add(await c.Scores.SubmitAsync(1));
            results.Add((await c.Scores.TopAsync()).Count);
            await c.Storage.SetAsync("k", "v");
            results.Add(await c.Storage.GetAsync("k"));
            results.Add(c.Today);
            results.Add(c.Caller.Handle);
            results.Add(c.Random is not null);
        });
        h.Loader.Door = door;
        h.Terminal.Keys("skip-title", "k").Type("line");
        var oracle = (await h.Content.GetDoorsAsync()).Find("oracle")!;
        await h.Doors.RunAsync(oracle, h.Session, CancellationToken.None);

        Assert.Equal<object?>(["k", "line", true, false, "{}", null, true, 1, "v", new DateOnly(2026, 11, 15), "Neo", true], results);
        Assert.Equal("_content/Pizon.Doors.Oracle/anim/intro.json", h.Terminal.Animations[0].Url);
        Assert.Equal(DoorHost.IntroLimit, h.Terminal.Animations[0].Options.MaxDuration);
        Assert.Contains(Transition.Dissolve, h.Terminal.Transitions);
        Assert.Contains("THE MODEM ORACLE", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Contains("Returning to PIZON BBS", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("DOOR CRASHED", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Equal("v", h.Terminal.Stored[NamespacedDoorStorage.KeyFor("oracle", "k")]);
        Assert.Contains("score:oracle:1:2026-11-15", h.Api.Calls);
        Assert.False(h.Terminal.LineModes[^1]);
    }

    /// <summary>A door that fails to load or throws shows DOOR CRASHED and the BBS carries on.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task CrashesAreContained()
    {
        var h = new Harness().Member();
        var vault = (await h.Content.GetDoorsAsync()).Find("vault")!;
        await h.Doors.RunAsync(vault, h.Session, CancellationToken.None);
        Assert.Contains("DOOR CRASHED", h.Terminal.Plain, StringComparison.Ordinal);
        Assert.Empty(h.Terminal.Animations);
        Assert.Equal([false], h.Terminal.LineModes);
    }

    /// <summary>Cancellation inside a door ends the call instead of being reported as a crash.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task CancellationPropagates()
    {
        var h = new Harness();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        h.Loader.Door = new LambdaDoor(_ => throw new OperationCanceledException(cts.Token));
        var oracle = (await h.Content.GetDoorsAsync()).Find("oracle")!;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Doors.RunAsync(oracle, h.Session, cts.Token));
        Assert.DoesNotContain("DOOR CRASHED", h.Terminal.Plain, StringComparison.Ordinal);
    }

    /// <summary>Intros outside the door's folder are not played; guests' scores are not sent.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GuardsAssetsAndScores()
    {
        var h = new Harness();
        h.Loader.Door = new NoopDoor();
        var entry = DoorManifest.Parse("""{ "doors": [ { "id": "x", "title": "X", "assemblies": ["X.wasm"], "entryType": "X", "intro": "../evil.json" } ] }""").Doors[0];
        await h.Doors.RunAsync(entry, h.Session, CancellationToken.None);
        Assert.Empty(h.Terminal.Animations);

        var scores = new ApiDoorScores(h.Api, "x", DoorCaller.Guest, "2026-11-15");
        Assert.False(await scores.SubmitAsync(5));
        h.Api.Scores = new(ApiStatus.Failed);
        Assert.Empty(await scores.TopAsync());
        Assert.Equal(["scores:x:2026-11-15"], h.Api.Calls);
    }

    /// <summary>Asset paths can't escape the door's folder.</summary>
    /// <param name="path">The requested path.</param>
    /// <param name="expected">The resolved URL, or null.</param>
    [Theory]
    [InlineData("anim/a.json", "_content/D/anim/a.json")]
    [InlineData("../a.json", null)]
    [InlineData("/a.json", null)]
    [InlineData("a\\b.json", null)]
    [InlineData("https://evil/a.json", null)]
    [InlineData(" ", null)]
    public void ResolvesAssets(string path, string? expected) => Assert.Equal(expected, DoorContextAdapter.ResolveAsset("_content/D/", path));

    /// <summary>The loader finds door types by name and rejects missing or wrong types.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LoadsDoorTypes()
    {
        static DoorManifestEntry Entry(string type) =>
            DoorManifest.Parse($$"""{ "doors": [ { "id": "x", "title": "X", "assemblies": ["Pizon.Web.Client.Tests.wasm"], "entryType": "{{type}}" } ] }""").Doors[0];
        var assemblies = new[] { typeof(NoopDoor).Assembly };

        Assert.IsType<NoopDoor>(LazyDoorLoader.Create(Entry(typeof(NoopDoor).FullName!), assemblies));
        Assert.Throws<InvalidOperationException>(() => LazyDoorLoader.Create(Entry("Nope.Door"), assemblies));
        Assert.Throws<InvalidOperationException>(() => LazyDoorLoader.Create(Entry(typeof(FakeApi).FullName!), assemblies));

        await using var bunit = new Bunit.BunitContext();
        var loader = new LazyDoorLoader(new Microsoft.AspNetCore.Components.WebAssembly.Services.LazyAssemblyLoader(bunit.JSInterop.JSRuntime));
        Assert.IsType<NoopDoor>(await loader.LoadAsync(Entry(typeof(NoopDoor).FullName!)));
    }

    /// <summary>The title card shows the door's name and description.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task BuildsTheTitleCard()
    {
        var oracle = (await new Harness().Content.GetDoorsAsync()).Find("oracle")!;
        var card = Pizon.Doors.Sdk.Text.PipeCodes.StripAnsi(DoorHost.TitleCard(oracle));
        Assert.Contains("THE MODEM ORACLE", card, StringComparison.Ordinal);
        Assert.Contains("Ask.", card, StringComparison.Ordinal);
    }
}
