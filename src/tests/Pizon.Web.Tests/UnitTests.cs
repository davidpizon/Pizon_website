using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pizon.Doors.Sdk;
using Pizon.Web.Auth;
using Pizon.Web.Components;
using Pizon.Web.Data;
using Pizon.Web.Endpoints;
using Pizon.Web.Hubs;
using Pizon.Web.Services;

namespace Pizon.Web.Tests;

/// <summary>
/// Tests for text cleaning and the word filter.
/// </summary>
public sealed class TextRulesTests
{
    private static readonly WordFilter Filter = new(Options.Create(new WordFilterOptions { Blocked = ["badword", " ", "BadWord"] }));

    /// <summary>Control and bidi characters go; newlines stay only where allowed.</summary>
    [Fact]
    public void CleansText()
    {
        Assert.Equal(string.Empty, TextRules.Clean(null, true));
        Assert.Equal("a\nb c", TextRules.Clean(" a  \r\nb\tc\u0007⁧ ", allowNewLines: true));
        Assert.Equal("a b", TextRules.Clean("a\rb", allowNewLines: false));
        Assert.True(TextRules.IsBidiControl('‪'));
        Assert.True(TextRules.IsBidiControl('⁩'));
        Assert.False(TextRules.IsBidiControl('a'));
    }

    /// <summary>Validation explains what is wrong.</summary>
    [Fact]
    public void ValidatesText()
    {
        Assert.Null(TextRules.Validate("hello", 10, Filter));
        Assert.Equal("message is empty", TextRules.Validate(string.Empty, 10, Filter));
        Assert.Equal("message is longer than 3 characters", TextRules.Validate("hello", 3, Filter));
        Assert.Equal("the word filter ate your message", TextRules.Validate("a BADWORD b", 99, Filter));
    }

    /// <summary>The filter matches whole words only, with underscores as breaks.</summary>
    [Fact]
    public void FiltersWholeWords()
    {
        Assert.False(Filter.IsClean("my_badword_handle"));
        Assert.False(Filter.IsClean("Casino night"));
        Assert.True(Filter.IsClean("badwords and casinos"));
        Assert.True(Filter.IsClean("Scunthorpe"));
    }
}

/// <summary>
/// Tests for the webhook notifier.
/// </summary>
public sealed class NotifierTests
{
    /// <summary>Discord webhooks get JSON; anything else is treated as ntfy with headers.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PostsToWebhooks()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        await Notifier(handler, "https://discord.com/api/webhooks/1/abc").NotifyAsync("Title", "Body");
        Assert.Contains("\"content\":\"**Title**\\nBody\"", handler.Bodies[0], StringComparison.Ordinal);

        await Notifier(handler, "https://ntfy.sh/topic").NotifyAsync("Title", "Body");
        Assert.Equal("Body", handler.Bodies[1]);
        Assert.Equal("Title", handler.Requests[1].Headers.GetValues("Title").Single());
    }

    /// <summary>No URL, bad responses, network errors and timeouts never throw.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NeverThrows()
    {
        await Notifier(new StubHandler(HttpStatusCode.OK), null).NotifyAsync("t", "m");
        await Notifier(new StubHandler(HttpStatusCode.InternalServerError), "https://ntfy.sh/x").NotifyAsync("t", "m");
        await Notifier(new StubHandler(new HttpRequestException("down")), "https://ntfy.sh/x").NotifyAsync("t", "m");
        await Notifier(new StubHandler(new TaskCanceledException("timeout")), "https://ntfy.sh/x").NotifyAsync("t", "m");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Notifier(new StubHandler(new TaskCanceledException("cancelled")), "https://ntfy.sh/x").NotifyAsync("t", "m", cts.Token));
    }

    /// <summary>Only real Discord webhook URLs count as Discord.</summary>
    /// <param name="url">The URL.</param>
    /// <param name="expected">Whether it is a Discord webhook.</param>
    [Theory]
    [InlineData("https://discord.com/api/webhooks/1/x", true)]
    [InlineData("https://discordapp.com/api/webhooks/1/x", true)]
    [InlineData("https://discord.com/other", false)]
    [InlineData("https://evil.com/api/webhooks/1/x", false)]
    public void RecognizesDiscord(string url, bool expected) => Assert.Equal(expected, WebhookNotifier.IsDiscordWebhook(new Uri(url)));

    private static WebhookNotifier Notifier(StubHandler handler, string? url) =>
        new(new HttpClient(handler, disposeHandler: false), Options.Create(new NotifyOptions { WebhookUrl = url is null ? null : new Uri(url) }), NullLogger<WebhookNotifier>.Instance);

    /// <summary>
    /// Answers every request with a status, or throws.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly Exception? error;

        /// <summary>
        /// Initializes a new instance of the <see cref="StubHandler"/> class that answers with a status.
        /// </summary>
        /// <param name="status">The status.</param>
        public StubHandler(HttpStatusCode status) => this.status = status;

        /// <summary>
        /// Initializes a new instance of the <see cref="StubHandler"/> class that throws.
        /// </summary>
        /// <param name="error">The exception.</param>
        public StubHandler(Exception error) => this.error = error;

        /// <summary>Gets the requests.</summary>
        public List<HttpRequestMessage> Requests { get; } = [];

        /// <summary>Gets the request bodies.</summary>
        public List<string> Bodies { get; } = [];

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            return error is null ? new HttpResponseMessage(status) : throw error;
        }
    }
}

/// <summary>
/// Tests for small helpers: data paths, auth helpers, score days, previews, the plain page and the door catalog.
/// </summary>
public sealed class HelperTests
{
    /// <summary>The data folder follows the connection string.</summary>
    [Fact]
    public void FindsTheDataFolder()
    {
        var root = Path.GetFullPath("root");
        Assert.Equal(Path.Combine(root, "App_Data"), DataPaths.DirectoryOf("Data Source=App_Data/bbs.db", root));
        Assert.Equal(Path.Combine(root, "App_Data"), DataPaths.DirectoryOf("Data Source=:memory:", root));
        Assert.Equal(Path.Combine(root, "App_Data"), DataPaths.DirectoryOf("Mode=Memory", root));
        var rooted = Path.Combine(Path.GetTempPath(), "x", "bbs.db");
        Assert.Equal(Path.GetDirectoryName(rooted), DataPaths.DirectoryOf($"Data Source={rooted}", root));
    }

    /// <summary>Return URLs must stay on this site.</summary>
    /// <param name="url">The requested URL.</param>
    /// <param name="expected">The URL used.</param>
    [Theory]
    [InlineData("/?resume=login", "/?resume=login")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    public void KeepsReturnUrlsLocal(string? url, string expected) => Assert.Equal(expected, AuthSetup.SafeReturnUrl(url));

    /// <summary>The Discord display name wins over the username; blanks are skipped.</summary>
    [Fact]
    public void ReadsDiscordNames()
    {
        static JsonElement J(string json) => JsonElement.Parse(json);
        Assert.Equal("Neo", AuthSetup.DiscordName(J("""{"global_name":"Neo","username":"neo1"}""")));
        Assert.Equal("neo1", AuthSetup.DiscordName(J("""{"global_name":" ","username":"neo1"}""")));
        Assert.Equal("neo1", AuthSetup.DiscordName(J("""{"global_name":null,"username":"neo1"}""")));
        Assert.Null(AuthSetup.DiscordName(J("""{}""")));
        Assert.Null(AuthSetup.DiscordName(J("""[]""")));
    }

    /// <summary>Score days must be real dates within a day of today.</summary>
    [Fact]
    public void ParsesScoreDays()
    {
        var today = new DateOnly(2026, 11, 15);
        Assert.Equal("2026-11-14", ScoreEndpoints.ParseDay("2026-11-14", today));
        Assert.Equal("2026-11-16", ScoreEndpoints.ParseDay("2026-11-16", today));
        Assert.Null(ScoreEndpoints.ParseDay("2026-11-17", today));
        Assert.Null(ScoreEndpoints.ParseDay(null, today));
        Assert.Null(ScoreEndpoints.ParseDay("15/11/2026", today));
    }

    /// <summary>Previews flatten and shorten text; times come back as UTC.</summary>
    [Fact]
    public void PreviewsAndTimes()
    {
        Assert.Equal("a b", BoardEndpoints.Preview("a\nb"));
        var preview = BoardEndpoints.Preview(new string('x', 200));
        Assert.Equal(120, preview.Length);
        Assert.EndsWith("...", preview, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.Zero, BoardEndpoints.Utc(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)).Offset);
        Assert.Throws<InvalidOperationException>(() => CurrentUser.Get(new DefaultHttpContext()));
    }

    /// <summary>The plain page lists the shipped bulletins and links, and copes with missing or odd files.</summary>
    [Fact]
    public void RendersThePlainPage()
    {
        using var shipped = new PhysicalFileProvider(RepoPaths.ClientWwwroot);
        var html = PlainEndpoint.Render(shipped);
        Assert.Contains("Bulletin #1", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("|14", html, StringComparison.Ordinal);

        var dir = Directory.CreateTempSubdirectory("pizon-plain");
        try
        {
            using var empty = new PhysicalFileProvider(dir.FullName);
            Assert.Contains("Dial in to the full BBS", PlainEndpoint.Render(empty), StringComparison.Ordinal);

            Directory.CreateDirectory(Path.Combine(dir.FullName, "content"));
            File.WriteAllText(Path.Combine(dir.FullName, "content", "bulletins.json"), "{ broken");
            File.WriteAllText(
                Path.Combine(dir.FullName, "content", "files.json"),
                """{ "areas": [ { "name": "<X>", "files": [ { "name": "a.txt", "url": null, "description": "d" }, { "name": "b", "url": "ftp://x/", "description": "e" } ] } ] }""");
            using var odd = new PhysicalFileProvider(dir.FullName);
            var oddHtml = PlainEndpoint.Render(odd);
            Assert.Contains("&lt;X&gt;", oddHtml, StringComparison.Ordinal);
            Assert.Contains("<li>a.txt - d</li>", oddHtml, StringComparison.Ordinal);
            Assert.Contains("<li>b - e</li>", oddHtml, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>The door catalog reads the shipped manifest and falls back to empty.</summary>
    [Fact]
    public void LoadsTheDoorCatalog()
    {
        var catalog = new FileDoorCatalog(new FolderEnvironment(RepoPaths.ClientWwwroot));
        var oracle = catalog.FindScored("oracle");
        Assert.NotNull(oracle);
        Assert.Equal(ScoreMode.DailyCount, oracle.ScoreMode);
        Assert.Null(catalog.FindScored("nope"));

        var dir = Directory.CreateTempSubdirectory("pizon-doors");
        try
        {
            using var none = new PhysicalFileProvider(dir.FullName);
            Assert.Empty(FileDoorCatalog.Load(none).Doors);
            Directory.CreateDirectory(Path.Combine(dir.FullName, "content"));
            File.WriteAllText(Path.Combine(dir.FullName, FileDoorCatalog.ManifestPath), "{ broken");
            using var broken = new PhysicalFileProvider(dir.FullName);
            Assert.Empty(FileDoorCatalog.Load(broken).Doors);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>Discord sign-in is configured only when both the id and secret are set.</summary>
    [Fact]
    public void DetectsDiscordSettings()
    {
        static IConfiguration C(params (string, string)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build();
        Assert.False(AuthSetup.DiscordConfigured(C()));
        Assert.False(AuthSetup.DiscordConfigured(C(("Discord:ClientId", "x"))));
        Assert.True(AuthSetup.DiscordConfigured(C(("Discord:ClientId", "x"), ("Discord:ClientSecret", "y"))));
    }
}

/// <summary>
/// Tests for the caller directory (who is who, and who is the SysOp).
/// </summary>
public sealed class CallerDirectoryTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly BbsDbContext db;

    /// <summary>
    /// Initializes a new instance of the <see cref="CallerDirectoryTests"/> class.
    /// </summary>
    public CallerDirectoryTests()
    {
        connection.Open();
        db = new BbsDbContext(new DbContextOptionsBuilder<BbsDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    /// <summary>Sign-ins create then update users, trimming long names.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RecordsSignIns()
    {
        var clock = new FixedClock();
        var callers = Directory(clock, "42", Microsoft.Extensions.Hosting.Environments.Production);
        var first = await callers.SignedInAsync("42", new string('n', 80));
        Assert.Equal(64, first.DiscordName!.Length);
        clock.Now = clock.Now.AddHours(1);
        var again = await callers.SignedInAsync("42", "Neo");
        Assert.Equal(first.Id, again.Id);
        Assert.Equal("Neo", again.DiscordName);
        Assert.Equal(clock.Now.UtcDateTime, again.LastLoginAt);
        await Assert.ThrowsAsync<ArgumentException>(() => callers.SignedInAsync(" ", null));

        Assert.Same(again, await callers.GetAsync(BbsClaims.PrincipalFor(again, "test")));
        Assert.Null(await callers.GetAsync(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    /// <summary>The configured Discord id is the SysOp; the dev SysOp only counts in Development; banned users never.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task FindsTheSysop()
    {
        var production = Directory(new FixedClock(), " 42 ", Microsoft.Extensions.Hosting.Environments.Production);
        var sysop = await production.SignedInAsync("42", "Boss");
        var dev = await production.SignedInAsync(CallerDirectory.DevSysopId, "Dev");
        Assert.True(production.IsSysop(sysop));
        Assert.False(production.IsSysop(dev));
        Assert.False(production.IsSysop(null));
        Assert.Equal(0, production.LevelOf(sysop));
        sysop.Handle = "Boss";
        Assert.Equal(255, production.LevelOf(sysop));
        sysop.Banned = true;
        Assert.False(production.IsSysop(sysop));
        Assert.Equal(10, production.LevelOf(sysop));
        Assert.Equal(0, production.LevelOf(null));

        var development = Directory(new FixedClock(), null, Microsoft.Extensions.Hosting.Environments.Development);
        Assert.True(development.IsSysop(dev));
        Assert.False(development.IsSysop(new BbsUser { DiscordId = "42" }));
    }

    private CallerDirectory Directory(TimeProvider clock, string? sysopId, string environment) =>
        new(db, Options.Create(new SysopOptions { DiscordUserId = sysopId }), new FolderEnvironment(Path.GetFullPath(".")) { EnvironmentName = environment }, clock);
}

/// <summary>
/// Tests for node numbers.
/// </summary>
public sealed class NodeRegistryTests
{
    /// <summary>The lowest free node is assigned and freed nodes are reused.</summary>
    [Fact]
    public void AssignsLowestFreeNode()
    {
        var nodes = new NodeRegistry();
        Assert.Equal(1, nodes.Assign("a"));
        Assert.Equal(2, nodes.Assign("b"));
        Assert.Equal(2, nodes.Assign("b"));
        Assert.Equal(3, nodes.Assign("c"));
        Assert.True(nodes.Release("b"));
        Assert.False(nodes.Release("b"));
        Assert.Equal([1, 3], nodes.Busy());
        Assert.Equal(2, nodes.Assign("d"));
    }
}

/// <summary>
/// Tests for the terminal look-and-feel settings.
/// </summary>
public sealed class TerminalOptionsTests
{
    /// <summary>The glow blur defaults to 0.1, is written as an invariant CSS number, and is clamped to 0..1.</summary>
    /// <param name="blur">The configured blur.</param>
    /// <param name="expected">The CSS value written to the page.</param>
    [Theory]
    [InlineData(TerminalOptions.DefaultGlowBlur, "0.1")]
    [InlineData(0.0, "0")]
    [InlineData(0.25, "0.25")]
    [InlineData(-3.0, "0")]
    [InlineData(7.0, "1")]
    [InlineData(double.NaN, "0.1")]
    [InlineData(double.PositiveInfinity, "0.1")]
    public void FormatsAndClampsGlowBlur(double blur, string expected)
    {
        Assert.Equal(expected, new TerminalOptions { GlowBlur = blur }.GlowBlurCss);
    }

    /// <summary>A comma-decimal culture doesn't leak into the CSS value.</summary>
    [Fact]
    public void UsesInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("0.35", new TerminalOptions { GlowBlur = 0.35 }.GlowBlurCss);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
