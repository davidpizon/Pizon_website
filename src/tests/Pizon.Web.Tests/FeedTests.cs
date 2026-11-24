using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pizon.Web.Client.Api;
using Pizon.Web.Data;
using Pizon.Web.Endpoints;
using Pizon.Web.Services.Discord;

namespace Pizon.Web.Tests;

/// <summary>
/// A fake Discord channel API: serves <c>channels/{id}/messages</c> from an in-memory channel the way Discord
/// does (newest first; with <c>after</c>, the oldest messages after that id), or a chosen failure.
/// </summary>
internal sealed class FakeDiscord : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Gets the channel id served.</summary>
    public string Channel { get; } = "555";

    /// <summary>Gets the messages in the channel.</summary>
    public List<DiscordMessage> Messages { get; } = [];

    /// <summary>Gets the requests received.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Gets or sets a status to answer instead of the messages.</summary>
    public HttpStatusCode? FailWith { get; set; }

    /// <summary>Gets or sets a body to send instead of the messages.</summary>
    public string? RawBody { get; set; }

    /// <summary>Gets or sets an exception to throw instead of answering.</summary>
    public Exception? Throw { get; set; }

    /// <summary>
    /// Creates a message.
    /// </summary>
    /// <param name="id">The snowflake.</param>
    /// <param name="content">The text.</param>
    /// <param name="type">The message type.</param>
    /// <returns>The message.</returns>
    public static DiscordMessage Message(long id, string content, int type = DiscordMessage.DefaultType) => new()
    {
        Id = id.ToString(CultureInfo.InvariantCulture),
        Type = type,
        Content = content,
        Timestamp = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(id),
        Author = new DiscordUser { Id = "1", Username = "davidpizon", GlobalName = "David" },
    };

    /// <summary>
    /// Adds messages with ids from <paramref name="first"/> on.
    /// </summary>
    /// <param name="first">The first id.</param>
    /// <param name="count">How many.</param>
    /// <returns>This fake.</returns>
    public FakeDiscord Add(long first, int count)
    {
        for (var i = 0; i < count; i++)
        {
            Messages.Add(Message(first + i, $"Title {first + i}\nBody {first + i}"));
        }

        return this;
    }

    /// <summary>
    /// Creates a client over this fake.
    /// </summary>
    /// <returns>The client.</returns>
    public HttpClient Client() => new(this) { BaseAddress = DiscordClient.ApiBase };

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);
        if (Throw is not null)
        {
            throw Throw;
        }

        if (FailWith is { } status)
        {
            return Task.FromResult(new HttpResponseMessage(status));
        }

        var uri = request.RequestUri!;
        Assert.Equal($"/api/v10/channels/{Channel}/messages", uri.AbsolutePath);
        var query = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        var limit = int.Parse(query["limit"], CultureInfo.InvariantCulture);
        IEnumerable<DiscordMessage> page = query.TryGetValue("after", out var after)
            ? Messages.Where(m => m.Snowflake > ulong.Parse(after, CultureInfo.InvariantCulture)).OrderBy(m => m.Snowflake).Take(limit)
            : Messages.OrderByDescending(m => m.Snowflake).Take(limit);
        var body = RawBody ?? JsonSerializer.Serialize(page.OrderByDescending(m => m.Snowflake).ToList(), Json);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

/// <summary>
/// Tests for <see cref="DiscordText"/>: turning Discord markdown into safe pipe-code bulletins.
/// </summary>
public sealed class DiscordTextTests
{
    private static readonly Dictionary<string, string> NoNames = [];

    /// <summary>The first line is the title and the rest the body; blank lines after the title are dropped.</summary>
    [Fact]
    public void SplitsTitleAndBody()
    {
        var (title, lines) = DiscordText.Convert(FakeDiscord.Message(1, "## Server move\n\n\nWe move **tonight**.\n-# small print\n> quoted"));
        Assert.Equal("Server move", title);
        Assert.Equal(["We move |15tonight|07.", "|08small print|07", "|08>|07 quoted"], lines);
    }

    /// <summary>A one-line message is its own body, and the header marker colors it.</summary>
    [Fact]
    public void OneLineIsItsOwnBody()
    {
        var (title, lines) = DiscordText.Convert(FakeDiscord.Message(1, "# Hello **all**"));
        Assert.Equal("Hello all", title);
        Assert.Equal(["|14Hello all|07"], lines);
    }

    /// <summary>Pipes and control characters people type can't become pipe codes.</summary>
    [Fact]
    public void EscapesWhatPeopleWrite()
    {
        var (title, lines) = DiscordText.Convert(FakeDiscord.Message(1, "|15fake|[x]\u001b[31m\nbody |04red"));
        Assert.Equal("||15fake||[x][31m", title);
        Assert.Equal(["body ||04red"], lines);
    }

    /// <summary>Mentions, emoji, timestamps, links, code and the other inline marks become plain words.</summary>
    [Fact]
    public void TurnsMarkupIntoText()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal) { ["42"] = "Neo|1" };
        Assert.Equal("@Neo||1 @user @role #channel", DiscordText.Inline("<@42> <@!7> <@&9> <#3>", names, color: true));
        Assert.Equal(":wave: :party:", DiscordText.Inline("<:wave:123> <a:party:456>", NoNames, color: true));
        Assert.Equal("2026-11-24 00:00 UTC and <t:99999999999999:R>", DiscordText.Inline("<t:1795478400:F> and <t:99999999999999:R>", NoNames, color: true));
        Assert.Equal("see https://pizon.com and docs (https://example.com/a)", DiscordText.Inline("see <https://pizon.com> and [docs](https://example.com/a)", NoNames, color: true));
        Assert.Equal("code u s spoiler", DiscordText.Inline("`code` __u__ ~~s~~ ||spoiler||", NoNames, color: true));
        Assert.Equal("|15bold|07 / bold", DiscordText.Inline("**bold**", NoNames, color: true) + " / " + DiscordText.Inline("**bold**", NoNames, color: false));
    }

    /// <summary>Mentioned users show their display name, falling back to the username.</summary>
    [Fact]
    public void UsesMentionNames()
    {
        var message = new DiscordMessage
        {
            Content = "Hi <@2> and <@3>",
            Mentions = [new DiscordUser { Id = "2", Username = "neo", GlobalName = "Neo" }, new DiscordUser { Id = "3", Username = "trinity" }, new DiscordUser { Username = "nobody" }],
        };
        Assert.Equal("Hi @Neo and @trinity", DiscordText.Convert(message).Title);
    }

    /// <summary>Code fences are dropped, the code stays; attachments and embeds become lines of their own.</summary>
    [Fact]
    public void AddsAttachmentsAndEmbeds()
    {
        var message = new DiscordMessage
        {
            Content = "```\nRelease\n```\nline",
            Attachments = [new DiscordAttachment { Filename = "a|b.png", ContentType = "image/png" }, new DiscordAttachment { Filename = "notes.zip" }],
            Embeds = [new DiscordEmbed { Title = "Card", Description = "# Big\nsmall", Url = "https://x.test/" }, new DiscordEmbed()],
        };
        var (title, lines) = DiscordText.Convert(message);
        Assert.Equal("Release", title);
        Assert.Equal(["line", "|15Card|07", "|14Big|07", "small", "|08https://x.test/|07", "|08[image: a||b.png]|07", "|08[file: notes.zip]|07"], lines);
    }

    /// <summary>A message with only a picture is titled by the file; with nothing at all it is "(no text)".</summary>
    [Fact]
    public void TitlesEmptyMessages()
    {
        var picture = new DiscordMessage { Content = "  ", Attachments = [new DiscordAttachment { Filename = "cat.jpg", ContentType = "image/jpeg" }] };
        Assert.Equal("cat.jpg", DiscordText.Convert(picture).Title);
        var (title, lines) = DiscordText.Convert(new DiscordMessage());
        Assert.Equal(DiscordText.Untitled, title);
        Assert.Empty(lines);
    }

    /// <summary>Long titles are cut to fit.</summary>
    [Fact]
    public void CutsLongTitles() =>
        Assert.True(Pizon.Doors.Sdk.Text.PipeCodes.VisibleLength(DiscordText.Convert(FakeDiscord.Message(1, new string('x', 200))).Title) <= DiscordText.TitleWidth);
}

/// <summary>
/// Tests for the Discord settings and the REST client.
/// </summary>
public sealed class DiscordClientTests
{
    /// <summary>Feeds need both a bot token and a channel; settings bind from configuration, names in any case.</summary>
    [Fact]
    public void BindsOptions()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Discord:BotToken"] = "t",
            ["Discord:PollMinutes"] = "3",
            ["Discord:Feeds:Bulletins:ChannelId"] = " 555 ",
            ["Discord:Feeds:Board:ChannelId"] = string.Empty,
        }).Build();
        var options = new DiscordFeedOptions();
        config.GetSection("Discord").Bind(options);

        Assert.Equal(3, options.PollMinutes);
        Assert.Equal("555", options.ChannelFor("bulletins"));
        Assert.Null(options.ChannelFor("board"));
        Assert.Null(options.ChannelFor("nope"));
        Assert.Equal(["bulletins"], options.EnabledFeeds());
        options.BotToken = " ";
        Assert.Null(options.ChannelFor("bulletins"));
        Assert.Empty(options.EnabledFeeds());
    }

    /// <summary>Reads messages as the bot, with the paging parameters, in Discord's snake_case.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ReadsMessages()
    {
        var fake = new FakeDiscord();
        fake.Messages.Add(FakeDiscord.Message(10, "a"));
        fake.Messages.Add(new DiscordMessage { Id = "11", Content = "b", EditedTimestamp = DateTimeOffset.UnixEpoch, Author = new DiscordUser { Username = "u", GlobalName = "G" } });
        using var http = fake.Client();
        var client = new DiscordClient(http, Options.Create(new DiscordFeedOptions { BotToken = "secret" }));

        var newest = await client.GetMessagesAsync(fake.Channel, null, 5);
        Assert.Equal(["11", "10"], newest.Select(m => m.Id));
        Assert.Equal("G", newest[0].Author.Name);
        Assert.Equal(DateTimeOffset.UnixEpoch, newest[0].EditedTimestamp);
        var after = await client.GetMessagesAsync(fake.Channel, "10", 100);
        Assert.Equal("11", Assert.Single(after).Id);
        Assert.Equal("Bot secret", fake.Requests[0].Headers.Authorization!.ToString());
        Assert.Contains("after=10", fake.Requests[1].RequestUri!.Query, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetMessagesAsync(fake.Channel, null, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetMessagesAsync(fake.Channel, null, 101));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetMessagesAsync(" ", null, 1));
    }

    /// <summary>Refusals, bad JSON, null JSON and network failures all become <see cref="DiscordUnavailableException"/> (or an empty page).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ReportsFailures()
    {
        var fake = new FakeDiscord { FailWith = HttpStatusCode.Unauthorized };
        using var http = fake.Client();
        var client = new DiscordClient(http, Options.Create(new DiscordFeedOptions { BotToken = "t" }));

        var refused = await Assert.ThrowsAsync<DiscordUnavailableException>(() => client.GetMessagesAsync(fake.Channel, null, 1));
        Assert.Contains("401", refused.Message, StringComparison.Ordinal);
        Assert.EndsWith("no reason given", refused.Message, StringComparison.Ordinal);
        Assert.Equal("Missing Access (code 50001)", DiscordClient.ErrorMessage("""{"message":"Missing Access","code":50001}"""));
        Assert.Equal("Unknown", DiscordClient.ErrorMessage("""{"message":"Unknown"}"""));
        Assert.Equal("no reason given", DiscordClient.ErrorMessage("""{"code":1}"""));
        Assert.Equal("no reason given", DiscordClient.ErrorMessage("<html>"));
        fake.FailWith = null;
        fake.RawBody = "not json";
        Assert.IsType<JsonException>((await Assert.ThrowsAsync<DiscordUnavailableException>(() => client.GetMessagesAsync(fake.Channel, null, 1))).InnerException, exactMatch: false);
        fake.RawBody = "null";
        Assert.Empty(await client.GetMessagesAsync(fake.Channel, null, 1));
        fake.Throw = new HttpRequestException("down");
        await Assert.ThrowsAsync<DiscordUnavailableException>(() => client.GetMessagesAsync(fake.Channel, null, 1));
        Assert.Equal("x", new DiscordUnavailableException("x").Message);
        Assert.NotNull(new DiscordUnavailableException().Message);
        Assert.False(new DiscordMessage { Id = "not a number" }.Snowflake > 0);
        Assert.False(new DiscordAttachment { Filename = "a" }.IsImage);
    }
}

/// <summary>
/// Tests for <see cref="FeedSync"/> and the feed endpoints over the real server and a throwaway database.
/// </summary>
public sealed class FeedSyncTests : IAsyncDisposable
{
    private readonly FakeDiscord discord = new();

    // No bot token here, so the server's own sync stays idle and each test drives one of its own.
    private readonly BbsFactory factory = new();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    /// <summary>
    /// The first check copies the whole history across pages, numbering text messages from 1 and skipping
    /// system messages; later checks continue the numbers; the server checks on its own at startup.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NumbersTheWholeHistory()
    {
        discord.Add(1000, 150);
        discord.Messages.Add(FakeDiscord.Message(1075, "joined", type: 7));
        await using var hosted = new BbsFactory(
            new()
            {
                ["Discord:BotToken"] = "t",
                ["Discord:Feeds:Bulletins:ChannelId"] = discord.Channel,
                ["Discord:PollMinutes"] = "60",
            },
            services: s => s.AddHttpClient<DiscordClient>().ConfigurePrimaryHttpMessageHandler(() => discord));
        await WaitForStateAsync(hosted);
        var sync = hosted.Services.GetRequiredService<FeedSync>();

        Assert.True(await sync.SyncAsync("Bulletins"));
        var numbers = await hosted.WithDbAsync(db => db.FeedPosts.OrderBy(p => p.Number).Select(p => new { p.Number, p.MessageId }).ToListAsync());
        Assert.Equal(Enumerable.Range(1, 150), numbers.Select(n => n.Number));
        Assert.Equal("1000", numbers[0].MessageId);
        Assert.Equal("1149", numbers[^1].MessageId);

        discord.Add(2000, 2);
        Assert.True(await sync.SyncAsync("bulletins"));
        var feed = await hosted.Caller().Get<FeedDto>("/api/feeds/bulletins");
        Assert.True(feed.Enabled);
        Assert.True(feed.Online);
        Assert.False((await factory.Caller().Get<FeedDto>("/api/feeds/bulletins")).Enabled);
        Assert.Equal(Feeds.ListSize, feed.Posts.Count);
        Assert.Equal([152, 151, 150], feed.Posts.Take(3).Select(p => p.Number));
        Assert.Equal("Title 2001", feed.Posts[0].Title);
        Assert.Equal(["Body 2001"], feed.Posts[0].Lines);
        Assert.Equal("David", feed.Posts[0].Author);
    }

    /// <summary>Edits of recent posts are copied; deleted posts disappear but keep their number.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task FollowsEditsAndDeletes()
    {
        discord.Add(1, 5);
        using var sync = Sync();
        Assert.True(await sync.SyncAsync(Feeds.Bulletins));

        discord.Messages[4] = new DiscordMessage
        {
            Id = "5",
            Content = "Fixed title\nnew body",
            Timestamp = discord.Messages[4].Timestamp,
            EditedTimestamp = discord.Messages[4].Timestamp.AddHours(1),
            Author = discord.Messages[4].Author,
        };
        discord.Messages.RemoveAt(2);
        Assert.True(await sync.SyncAsync(Feeds.Bulletins));

        var client = factory.Caller();
        var feed = await client.Get<FeedDto>("/api/feeds/bulletins");
        Assert.Equal([5, 4, 2, 1], feed.Posts.Select(p => p.Number));
        var edited = await client.Get<FeedPostDto>("/api/feeds/bulletins/5");
        Assert.Equal("Fixed title", edited.Title);
        Assert.NotNull(edited.EditedAt);
        using var deleted = await client.GetAsync(new Uri("/api/feeds/bulletins/3", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.True(await factory.WithDbAsync(db => db.FeedPosts.AnyAsync(p => p.Number == 3 && p.Deleted)));

        var unchanged = await factory.WithDbAsync(db => db.FeedPosts.SingleAsync(p => p.Number == 4));
        Assert.Null(unchanged.EditedAt);
    }

    /// <summary>
    /// Posts saved while Discord sent no text (Message Content Intent off) get their text at the next check,
    /// though Discord never marks them edited.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task FillsInTextThatArrivesLater()
    {
        discord.Messages.Add(FakeDiscord.Message(1, string.Empty));
        using var sync = Sync();
        Assert.True(await sync.SyncAsync(Feeds.Bulletins));
        Assert.Equal(DiscordText.Untitled, (await factory.Caller().Get<FeedPostDto>("/api/feeds/bulletins/1")).Title);

        discord.Messages[0] = FakeDiscord.Message(1, "Now with words\nand a body");
        Assert.True(await sync.SyncAsync(Feeds.Bulletins));
        var post = await factory.Caller().Get<FeedPostDto>("/api/feeds/bulletins/1");
        Assert.Equal("Now with words", post.Title);
        Assert.Equal(["and a body"], post.Lines);
        Assert.Null(post.EditedAt);
    }

    /// <summary>When Discord fails, the saved posts stay and the feed reports offline until a check works again.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task KeepsPostsWhileDiscordIsDown()
    {
        discord.Add(1, 2);
        using var sync = Sync();
        Assert.True(await sync.SyncAsync(Feeds.Bulletins));

        discord.FailWith = HttpStatusCode.ServiceUnavailable;
        discord.Add(3, 1);
        Assert.False(await sync.SyncAsync(Feeds.Bulletins));
        var feed = await factory.Caller().Get<FeedDto>("/api/feeds/bulletins");
        Assert.False(feed.Online);
        Assert.Equal(2, feed.Posts.Count);

        discord.FailWith = null;
        Assert.True(await sync.SyncAsync(Feeds.Bulletins));
        Assert.True((await factory.Caller().Get<FeedDto>("/api/feeds/bulletins")).Online);
    }

    /// <summary>A feed with no channel is not read; unknown feeds list nothing; odd names and numbers are not found.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task IgnoresUnconfiguredFeeds()
    {
        using var sync = Sync();
        Assert.False(await sync.SyncAsync("board"));
        await Assert.ThrowsAsync<ArgumentException>(() => sync.SyncAsync(" "));

        var client = factory.Caller();
        var board = await client.Get<FeedDto>("/api/feeds/board");
        Assert.False(board.Enabled);
        Assert.True(board.Online);
        Assert.Empty(board.Posts);
        using var tooLong = await client.GetAsync(new Uri("/api/feeds/" + new string('x', 40), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, tooLong.StatusCode);
        using var unknown = await client.GetAsync(new Uri("/api/feeds/bulletins/77", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    /// <summary>The plain page lists the Discord bulletins before the file bulletins, without pipe codes.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ShowsDiscordBulletinsOnThePlainPage()
    {
        discord.Messages.Add(FakeDiscord.Message(1, "Plain **news**\nmore"));
        using var sync = Sync();
        Assert.True(await sync.SyncAsync(Feeds.Bulletins));

        using var response = await factory.Caller().GetAsync(new Uri("/plain", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Bulletin #D1: Plain news", html, StringComparison.Ordinal);
        Assert.Contains("<p>more</p>", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("#D1", StringComparison.Ordinal) < html.IndexOf("Bulletin #1", StringComparison.Ordinal));
    }

    /// <summary>A saved post without body lines has an empty line list.</summary>
    [Fact]
    public void MapsEmptyBodies() =>
        Assert.Empty(FeedEndpoints.ToDto(new FeedPost { Number = 1, Title = "t", PostedAt = DateTime.UtcNow }).Lines);

    private FeedSync Sync()
    {
        var options = Options.Create(new DiscordFeedOptions { BotToken = "t" });
        options.Value.Feeds["Bulletins"] = new DiscordFeedChannel { ChannelId = discord.Channel };
        return new FeedSync(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new DiscordClient(new HttpClient(discord, disposeHandler: false) { BaseAddress = DiscordClient.ApiBase }, options),
            options,
            factory.Clock,
            NullLogger<FeedSync>.Instance);
    }

    private static async Task WaitForStateAsync(BbsFactory hosted)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await hosted.WithDbAsync(db => db.FeedStates.AnyAsync()))
        {
            Assert.True(DateTime.UtcNow < deadline, "the server never checked the feed");
            await Task.Delay(20);
        }
    }
}
