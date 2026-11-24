using System.Net;
using System.Text;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Doors;
using Pizon.Web.Client.Screens;
using Pizon.Web.Client.Screens.Hosting;
using Pizon.Web.Client.Services;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// A scripted terminal: keys come from a queue, and everything written is recorded.
/// </summary>
internal sealed class FakeTerminal : ITerminal, IBrowserStorage
{
    private readonly Queue<string> keys = new();
    private readonly StringBuilder output = new();

    /// <summary>Gets the stored browser values.</summary>
    public Dictionary<string, string> Stored { get; } = [];

    /// <summary>Gets the transitions played, in order.</summary>
    public List<Transition> Transitions { get; } = [];

    /// <summary>Gets the animations played, in order.</summary>
    public List<(string Url, AnimationOptions Options)> Animations { get; } = [];

    /// <summary>Gets every slot animation started, in order.</summary>
    public List<(string Name, SlotAnimation Animation)> SlotsStarted { get; } = [];

    /// <summary>Gets every slot stopped, in order.</summary>
    public List<string> SlotsStopped { get; } = [];

    /// <summary>Gets the slots playing now (a clear-screen write stops them all, as in the browser).</summary>
    public Dictionary<string, SlotAnimation> Playing { get; } = [];

    /// <summary>Gets the URLs opened in a new tab.</summary>
    public List<Uri> Opened { get; } = [];

    /// <summary>Gets the settings applied.</summary>
    public List<TerminalSettings> Applied { get; } = [];

    /// <summary>Gets the line-mode changes.</summary>
    public List<bool> LineModes { get; } = [];

    /// <summary>Gets art files by URL.</summary>
    public Dictionary<string, string> Art { get; } = [];

    /// <summary>Gets or sets a value indicating whether animations report that they played.</summary>
    public bool AnimationResult { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether waits are skipped.</summary>
    public bool Skip { get; set; }

    /// <summary>Gets or sets the RIP card's result.</summary>
    public string? RipKey { get; set; }

    /// <summary>Gets or sets the handshake audio length.</summary>
    public TimeSpan Handshake { get; set; }

    /// <summary>Gets or sets the caller's local date.</summary>
    public DateOnly Today { get; set; } = new(2026, 11, 15);

    /// <summary>Gets how many beeps sounded.</summary>
    public int Beeps { get; private set; }

    /// <summary>Gets how many times audio was stopped.</summary>
    public int AudioStops { get; private set; }

    /// <summary>Gets how many times pending keys were discarded.</summary>
    public int Discards { get; private set; }

    /// <summary>Gets the total delay requested.</summary>
    public TimeSpan Delayed { get; private set; }

    /// <summary>Gets everything written, as raw ANSI.</summary>
    public string Output => output.ToString();

    /// <summary>Gets each write (not transitions), in order.</summary>
    public List<string> Writes { get; } = [];

    /// <summary>Gets everything written, without escape sequences.</summary>
    public string Plain => PipeCodes.StripAnsi(Output);

    /// <summary>
    /// Queues keys for the screen to read.
    /// </summary>
    /// <param name="queued">The keys.</param>
    /// <returns>This terminal.</returns>
    public FakeTerminal Keys(params string[] queued)
    {
        foreach (var k in queued)
        {
            keys.Enqueue(k);
        }

        return this;
    }

    /// <summary>
    /// Queues each character of a string as a key, then Enter.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <returns>This terminal.</returns>
    public FakeTerminal Type(string text) => Press(text).Keys(DoorKeys.Enter);

    /// <summary>
    /// Queues each character of a string as a key, without Enter.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <returns>This terminal.</returns>
    public FakeTerminal Press(string text)
    {
        foreach (var c in text)
        {
            keys.Enqueue(c.ToString());
        }

        return this;
    }

    /// <inheritdoc />
    public Task WriteAsync(string ansi, CancellationToken cancellationToken = default)
    {
        Writes.Add(ansi);
        output.Append(ansi);
        StopSlotsOnClear(ansi);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task TransitionAsync(Transition effect, string ansi, CancellationToken cancellationToken = default)
    {
        Transitions.Add(effect);
        output.Append(ansi);
        StopSlotsOnClear(ansi);
        return Task.CompletedTask;
    }

    private void StopSlotsOnClear(string ansi)
    {
        if (ansi.Contains(Ansi.ClearScreen, StringComparison.Ordinal))
        {
            Playing.Clear();
        }
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default) => WriteAsync(Ansi.ClearScreen, cancellationToken);

    /// <summary>Gets or sets a token source cancelled (instead of failing) when the scripted keys run out.</summary>
    public CancellationTokenSource? StopWhenEmpty { get; set; }

    /// <summary>Gets or sets a callback run for each key read.</summary>
    public Action<string>? OnRead { get; set; }

    /// <summary>A scripted key that makes the read throw, to simulate a crash.</summary>
    public const string Crash = "!crash";

    /// <inheritdoc />
    public Task<string> ReadKeyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (keys.Count == 0)
        {
            if (StopWhenEmpty is { } stop)
            {
                stop.Cancel();
                throw new OperationCanceledException(stop.Token);
            }

            throw new InvalidOperationException("The screen read more keys than the test scripted.");
        }

        var key = keys.Dequeue();
        OnRead?.Invoke(key);
        return key == Crash ? throw new InvalidOperationException("crash") : Task.FromResult(key);
    }

    /// <inheritdoc />
    public Task<string?> ReadKeyAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        Task.FromResult(keys.Count > 0 ? keys.Dequeue() : null);

    /// <inheritdoc />
    public void DiscardPendingKeys() => Discards++;

    /// <inheritdoc />
    public Task<bool> WaitOrSkipAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        Delayed += duration;
        return Task.FromResult(Skip);
    }

    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        Delayed += delay;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> PlayAnimationAsync(string url, AnimationOptions options, CancellationToken cancellationToken = default)
    {
        Animations.Add((url, options));
        return Task.FromResult(AnimationResult);
    }

    /// <inheritdoc />
    public Task<string?> LoadArtAsync(string url, CancellationToken cancellationToken = default) =>
        Task.FromResult(Art.TryGetValue(url, out var art) ? art : null);

    /// <inheritdoc />
    public Task<string?> ShowRipAsync(string url, CancellationToken cancellationToken = default) => Task.FromResult(RipKey);

    /// <inheritdoc />
    public Task SetLineModeAsync(bool enabled)
    {
        LineModes.Add(enabled);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ApplySettingsAsync(TerminalSettings settings)
    {
        Applied.Add(settings);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TimeSpan> PlayHandshakeAsync() => Task.FromResult(Handshake);

    /// <inheritdoc />
    public Task StopAudioAsync()
    {
        AudioStops++;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task BeepAsync()
    {
        Beeps++;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartSlotAsync(string name, SlotAnimation animation)
    {
        SlotsStarted.Add((name, animation));
        Playing[name] = animation;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopSlotAsync(string name)
    {
        SlotsStopped.Add(name);
        Playing.Remove(name);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OpenUrlAsync(Uri url)
    {
        Opened.Add(url);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DateOnly> GetLocalDateAsync() => Task.FromResult(Today);

    /// <inheritdoc />
    public Task<string?> GetAsync(string key) => Task.FromResult(Stored.TryGetValue(key, out var v) ? v : null);

    /// <inheritdoc />
    public Task SetAsync(string key, string value)
    {
        Stored[key] = value;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A configurable server API that records what screens send.
/// </summary>
internal sealed class FakeApi : IBbsApi
{
    /// <summary>Gets or sets the identity returned by <c>/api/me</c>.</summary>
    public MeResponse Me { get; set; } = MeResponse.Guest with { DiscordEnabled = true };

    /// <summary>Gets or sets the result for writes.</summary>
    public ApiResult WriteResult { get; set; } = new(ApiStatus.Ok);

    /// <summary>Gets or sets the approved messages.</summary>
    public ApiResult<IReadOnlyList<MessageDto>> Messages { get; set; } = new(ApiStatus.Ok, []);

    /// <summary>Gets or sets the recent callers.</summary>
    public ApiResult<IReadOnlyList<CallerDto>> Callers { get; set; } = new(ApiStatus.Ok, []);

    /// <summary>Gets or sets the score boards.</summary>
    public ApiResult<IReadOnlyList<ScoreEntry>> Scores { get; set; } = new(ApiStatus.Ok, []);

    /// <summary>Gets or sets the moderation queue.</summary>
    public ApiResult<IReadOnlyList<PendingMessageDto>> Queue { get; set; } = new(ApiStatus.Ok, []);

    /// <summary>Gets or sets the pages inbox.</summary>
    public ApiResult<IReadOnlyList<SysopPageDto>> Pages { get; set; } = new(ApiStatus.Ok, []);

    /// <summary>Gets or sets the Discord bulletins feed.</summary>
    public ApiResult<FeedDto> Feed { get; set; } = new(ApiStatus.Ok, FeedDto.None);

    /// <summary>Gets the Discord posts that can be opened by number.</summary>
    public List<FeedPostDto> FeedPosts { get; } = [];

    /// <summary>Gets every write call, as "name:argument".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Gets the handle results to return, in order (then <see cref="WriteResult"/>).</summary>
    public Queue<ApiResult> HandleResults { get; } = new();

    /// <inheritdoc />
    public Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default) => Task.FromResult(Me);

    /// <inheritdoc />
    public Task<ApiResult> SetHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        Calls.Add("handle:" + handle);
        var result = HandleResults.Count > 0 ? HandleResults.Dequeue() : WriteResult;
        if (result.IsOk)
        {
            Me = Me with { Handle = handle };
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<ApiResult> LogoutAsync(CancellationToken cancellationToken = default) => Record("logout");

    /// <inheritdoc />
    public Task<ApiResult<IReadOnlyList<MessageDto>>> GetMessagesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Messages);

    /// <inheritdoc />
    public Task<ApiResult> PostMessageAsync(PostMessageRequest request, CancellationToken cancellationToken = default) =>
        Record($"post:{request?.ParentId}:{request?.Body}");

    /// <inheritdoc />
    public Task<ApiResult> PageSysopAsync(PageSysopRequest request, CancellationToken cancellationToken = default) => Record("page:" + request?.Message);

    /// <inheritdoc />
    public Task<ApiResult<IReadOnlyList<CallerDto>>> GetRecentCallersAsync(CancellationToken cancellationToken = default) => Task.FromResult(Callers);

    /// <inheritdoc />
    public Task<ApiResult<IReadOnlyList<ScoreEntry>>> GetScoresAsync(string doorId, string day, CancellationToken cancellationToken = default)
    {
        Calls.Add($"scores:{doorId}:{day}");
        return Task.FromResult(Scores);
    }

    /// <inheritdoc />
    public Task<ApiResult> SubmitScoreAsync(string doorId, ScoreSubmitRequest request, CancellationToken cancellationToken = default) =>
        Record($"score:{doorId}:{request?.Score}:{request?.Day}");

    /// <inheritdoc />
    public Task<ApiResult<IReadOnlyList<PendingMessageDto>>> GetModerationQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult(Queue);

    /// <inheritdoc />
    public Task<ApiResult> ModerateAsync(long messageId, bool approve, CancellationToken cancellationToken = default) =>
        Record($"moderate:{messageId}:{approve}");

    /// <inheritdoc />
    public Task<ApiResult> DeleteMessageAsync(long messageId, CancellationToken cancellationToken = default) => Record($"delete:{messageId}");

    /// <inheritdoc />
    public Task<ApiResult<IReadOnlyList<SysopPageDto>>> GetPagesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Pages);

    /// <inheritdoc />
    public Task<ApiResult> MarkPageReadAsync(long pageId, CancellationToken cancellationToken = default) => Record($"read:{pageId}");

    /// <inheritdoc />
    public Task<ApiResult> BanUserAsync(long userId, CancellationToken cancellationToken = default) => Record($"ban:{userId}");

    /// <inheritdoc />
    public Task<ApiResult<FeedDto>> GetFeedAsync(string feed, CancellationToken cancellationToken = default) => Task.FromResult(Feed);

    /// <inheritdoc />
    public Task<ApiResult<FeedPostDto>> GetFeedPostAsync(string feed, int number, CancellationToken cancellationToken = default) =>
        Task.FromResult(FeedPosts.FirstOrDefault(p => p.Number == number) is { } post
            ? new ApiResult<FeedPostDto>(ApiStatus.Ok, post)
            : new ApiResult<FeedPostDto>(ApiStatus.Forbidden, null));

    private Task<ApiResult> Record(string call)
    {
        Calls.Add(call);
        return Task.FromResult(WriteResult);
    }
}

/// <summary>
/// Presence with settable nodes.
/// </summary>
internal sealed class FakePresence : IPresence
{
    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public int NodeNumber { get; set; } = 1;

    /// <inheritdoc />
    public IReadOnlyList<int> Nodes { get; set; } = [1];

    /// <inheritdoc />
    public int Online => Nodes.Count;

    /// <summary>Gets how many times the caller connected.</summary>
    public int Connects { get; private set; }

    /// <summary>Gets how many times the caller disconnected.</summary>
    public int Disconnects { get; private set; }

    /// <summary>
    /// Changes the busy nodes and raises <see cref="Changed"/>.
    /// </summary>
    /// <param name="nodes">The new nodes.</param>
    public void Set(params int[] nodes)
    {
        Nodes = nodes;
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Connects++;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisconnectAsync()
    {
        Disconnects++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Records full-page navigations and in-app address changes, and can play a Back/Forward move.
/// </summary>
internal sealed class FakeNavigator : IExternalNavigator
{
    /// <inheritdoc />
    public event EventHandler<Uri>? LocationChanged;

    /// <inheritdoc />
    public Uri CurrentUri { get; set; } = new("http://localhost/");

    /// <summary>Gets the full-page navigations, in order.</summary>
    public List<string> Navigations { get; } = [];

    /// <summary>Gets the in-app address changes, in order.</summary>
    public List<(string Path, bool Replace)> Moves { get; } = [];

    /// <summary>Gets or sets a callback run after each in-app address change.</summary>
    public Action<string>? OnNavigate { get; set; }

    /// <inheritdoc />
    public void NavigateAway(string url) => Navigations.Add(url);

    /// <inheritdoc />
    public void Navigate(string path, bool replace)
    {
        Moves.Add((path, replace));
        Go(path);
        OnNavigate?.Invoke(path);
    }

    /// <summary>
    /// Changes the address as the browser would (Back, Forward or a typed address) and raises the event.
    /// </summary>
    /// <param name="path">The new path.</param>
    public void Go(string path)
    {
        CurrentUri = new Uri(new Uri("http://localhost/"), path);
        LocationChanged?.Invoke(this, CurrentUri);
    }
}

/// <summary>
/// Serves canned responses by relative path; anything else is 404.
/// </summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    /// <summary>Gets the responses by path (without the leading slash).</summary>
    public Dictionary<string, (HttpStatusCode Status, string Body)> Responses { get; } = [];

    /// <summary>Gets the requests received.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Gets the request bodies received.</summary>
    public List<string> Bodies { get; } = [];

    /// <summary>Gets or sets an exception to throw instead of answering.</summary>
    public Exception? Throw { get; set; }

    /// <summary>
    /// Creates a client for this handler.
    /// </summary>
    /// <returns>A client with base address <c>http://localhost/</c>.</returns>
    public HttpClient Client() => new(this) { BaseAddress = new Uri("http://localhost/") };

    /// <summary>
    /// Adds a response.
    /// </summary>
    /// <param name="path">The path (with query, without the leading slash).</param>
    /// <param name="body">The body.</param>
    /// <param name="status">The status.</param>
    /// <returns>This handler.</returns>
    public FakeHttp With(string path, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        Responses[path] = (status, body);
        return this;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);
        if (request.Content is not null)
        {
            Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }

        if (Throw is not null)
        {
            throw Throw;
        }

        var key = request.RequestUri!.PathAndQuery.TrimStart('/');
        return Responses.TryGetValue(key, out var r)
            ? new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) };
    }
}

/// <summary>
/// A door loader that hands out a given door, or fails.
/// </summary>
internal sealed class FakeDoorLoader : IDoorLoader
{
    /// <summary>Gets or sets the door to return; <see langword="null"/> makes loading fail.</summary>
    public IDoor? Door { get; set; }

    /// <inheritdoc />
    public Task<IDoor> LoadAsync(DoorManifestEntry entry, CancellationToken cancellationToken = default) =>
        Door is null ? throw new InvalidOperationException("no door") : Task.FromResult(Door);
}

/// <summary>
/// A door that runs a callback.
/// </summary>
/// <param name="run">What the door does.</param>
internal sealed class LambdaDoor(Func<IDoorContext, Task> run) : IDoor
{
    /// <summary>Gets the context the door last ran with.</summary>
    public IDoorContext? LastContext { get; private set; }

    /// <inheritdoc />
    public string Id => "test";

    /// <inheritdoc />
    public string Title => "Test Door";

    /// <inheritdoc />
    public Task RunAsync(IDoorContext context, CancellationToken cancellationToken)
    {
        LastContext = context;
        return run(context);
    }
}

/// <summary>
/// A clock fixed at a settable time (UTC).
/// </summary>
internal sealed class FixedTime : TimeProvider
{
    /// <summary>Gets or sets the current time.</summary>
    public DateTimeOffset Now { get; set; } = new(2026, 11, 15, 22, 30, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Everything a screen test needs, wired together with sample content.
/// </summary>
internal sealed class Harness
{
    /// <summary>Sample door manifest JSON.</summary>
    public const string DoorsJson = """
        { "doors": [
          { "id": "oracle", "title": "The Modem Oracle", "description": "Ask.", "assemblies": ["Pizon.Doors.Oracle.wasm"],
            "entryType": "Pizon.Doors.Oracle.ModemOracle", "scored": true, "scoreMode": "dailyCount", "scoreTitle": "SEEKERS", "intro": "anim/intro.json" },
          { "id": "vault", "title": "The Vault", "description": "Members only.", "assemblies": ["Vault.wasm"], "entryType": "X.Vault", "requiresAuth": true }
        ] }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="Harness"/> class.
    /// </summary>
    public Harness()
    {
        Http.With("content/bulletins.json", """
            { "sysop": "David Pizon", "mood": "Calm", "location": "Grid", "tagline": "Tag",
              "bulletins": [
                { "number": 1, "title": "Now", "updated": "today", "lines": ["|14Hello|07 world", "a very long line that goes on and on and on and on and on and on and on and on and on and on"] },
                { "number": 2, "title": "About", "cam": true, "lines": ["Bio"] } ] }
            """);
        Http.With("content/files.json", """
            { "areas": [ { "name": "TOOLS", "description": "Tools", "files": [
              { "name": "good.zip", "size": 1024, "description": "A good file", "url": "https://example.com/good" },
              { "name": "gone.txt", "size": 5, "description": "Missing", "url": null } ] } ] }
            """);
        Http.With("content/halloffame.json", """
            { "entries": [ { "handle": "Legend", "clearance": "LVL 99", "note": "Wrote code", "url": "https://example.com/legend" },
                           { "handle": "Plain", "note": "No link" } ] }
            """);
        Http.With("content/doors.json", DoorsJson);
        Content = new ContentStore(Http.Client());
        Doors = new DoorHost(Terminal, Api, Content, Terminal, Loader);
        Context = new ScreenContext(Terminal, Api, Content, Presence, Navigator, Terminal, Doors, Time);
        Host = new BbsHost(Navigator);
        Host.Attach(Context);
    }

    /// <summary>Gets the terminal.</summary>
    public FakeTerminal Terminal { get; } = new();

    /// <summary>Gets the API.</summary>
    public FakeApi Api { get; } = new();

    /// <summary>Gets presence.</summary>
    public FakePresence Presence { get; } = new();

    /// <summary>Gets the navigator.</summary>
    public FakeNavigator Navigator { get; } = new();

    /// <summary>Gets the content HTTP handler.</summary>
    public FakeHttp Http { get; } = new();

    /// <summary>Gets the door loader.</summary>
    public FakeDoorLoader Loader { get; } = new();

    /// <summary>Gets the clock.</summary>
    public FixedTime Time { get; } = new();

    /// <summary>Gets the content store.</summary>
    public ContentStore Content { get; }

    /// <summary>Gets the door host.</summary>
    public DoorHost Doors { get; }

    /// <summary>Gets the screen context.</summary>
    public ScreenContext Context { get; }

    /// <summary>Gets the page host (attached to the fake terminal).</summary>
    public BbsHost Host { get; }

    /// <summary>Gets the call (the host's).</summary>
    public BbsSession Session => Host.Session;

    /// <summary>
    /// Makes the caller a signed-in member (or the SysOp).
    /// </summary>
    /// <param name="sysop">Whether the caller is the SysOp.</param>
    /// <returns>This harness.</returns>
    public Harness Member(bool sysop = false)
    {
        Api.Me = new MeResponse(true, sysop ? "SysOp1" : "Neo", "Neo", sysop ? 255 : 10, sysop, true, true);
        Session.Me = Api.Me;
        return this;
    }
}

/// <summary>
/// A door with a parameterless constructor that does nothing, for loader tests.
/// </summary>
internal sealed class NoopDoor : IDoor
{
    /// <inheritdoc />
    public string Id => "noop";

    /// <inheritdoc />
    public string Title => "Noop";

    /// <inheritdoc />
    public Task RunAsync(IDoorContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}
