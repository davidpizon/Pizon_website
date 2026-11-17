using System.Net;
using Microsoft.EntityFrameworkCore;
using Pizon.Doors.Sdk;
using Pizon.Web.Client.Api;
using Pizon.Web.Data;

namespace Pizon.Web.Tests;

/// <summary>
/// End-to-end tests of the API through the real server pipeline.
/// </summary>
public sealed class ApiTests : IAsyncDisposable
{
    private readonly BbsFactory factory = new();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    /// <summary>Guests are told who they are and are refused every write.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GuestsAreReadOnly()
    {
        using var guest = factory.Caller();
        var me = await guest.Get<MeResponse>("/api/me");
        Assert.False(me.Authenticated);
        Assert.True(me.DevLoginEnabled);
        Assert.False(me.DiscordEnabled);

        foreach (var url in new[] { "/api/messages", "/api/page-sysop", "/api/scores/oracle", "/api/me/handle" })
        {
            using var response = await guest.PostJson(url, new { body = "hi", message = "hi", score = 1, day = factory.Clock.Today, handle = "Neo" });
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, url);
        }

        Assert.Empty(await guest.Get<List<MessageDto>>("/api/messages"));
        Assert.Empty(await guest.Get<List<CallerDto>>("/api/callers/recent"));
        Assert.True(File.Exists(factory.DatabasePath));
    }

    /// <summary>Writes without the anti-CSRF header are rejected before anything else.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task WritesNeedTheCsrfHeader()
    {
        using var client = await factory.SignInAsync("tester", "Neo");
        using var post = await client.PostJson("/api/messages", new { body = "hi" }, csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        using var logout = await client.PostJson("/auth/logout", csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Empty(factory.Notifier.Sent);
    }

    /// <summary>New users must pick a valid, clean, unreserved, unique handle before posting.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task HandleRules()
    {
        using var sysop = await factory.SignInAsync("sysop", "Neo");
        using var tester = await factory.SignInAsync("tester", null);
        var me = await tester.Get<MeResponse>("/api/me");
        Assert.True(me.Authenticated);
        Assert.Null(me.Handle);
        Assert.Equal("Dev_Tester", me.SuggestedHandle);
        Assert.Equal(SecurityLevels.Guest, me.Level);

        using var early = await tester.PostJson("/api/messages", new { body = "hi" });
        Assert.Equal(HttpStatusCode.Forbidden, early.StatusCode);
        Assert.Contains("choose a handle first", await early.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        foreach (var (handle, status) in new[]
        {
            ("ab", HttpStatusCode.BadRequest),
            ("guest", HttpStatusCode.BadRequest),
            ("SysOp", HttpStatusCode.BadRequest),
            ("casino-king", HttpStatusCode.BadRequest),
            ("neo", HttpStatusCode.Conflict),
            ("Trinity", HttpStatusCode.OK),
            ("Trinity_2", HttpStatusCode.OK),
        })
        {
            using var response = await tester.PostJson("/api/me/handle", new { handle });
            Assert.True(status == response.StatusCode, handle);
        }

        me = await tester.Get<MeResponse>("/api/me");
        Assert.Equal("Trinity_2", me.Handle);
        Assert.Equal(SecurityLevels.User, me.Level);
        var boss = await sysop.Get<MeResponse>("/api/me");
        Assert.True(boss.IsSysop);
        Assert.Equal(SecurityLevels.Sysop, boss.Level);
    }

    /// <summary>
    /// A post waits for review, the SysOp approves it, replies thread under it, and deleting removes the thread.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ModerationFlow()
    {
        using var tester = await factory.SignInAsync("tester", "Neo");
        using var sysop = await factory.SignInAsync("sysop", "Boss");

        using (var post = await tester.PostJson("/api/messages", new { body = "  first\r\n‮post\t!  " }))
        {
            Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        }

        Assert.Contains("PIZON BBS: message waiting for review|Neo: first post !", factory.Notifier.Sent);
        Assert.Empty(await tester.Get<List<MessageDto>>("/api/messages"));

        var queue = await sysop.Get<List<PendingMessageDto>>("/api/sysop/queue");
        var pending = Assert.Single(queue);
        Assert.Equal("first\npost !", pending.Body);
        Assert.Equal("Neo", pending.Handle);

        using (var approve = await sysop.PostJson($"/api/sysop/messages/{pending.Id}/moderate", new { approve = true }))
        {
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        }

        var listed = Assert.Single(await tester.Get<List<MessageDto>>("/api/messages"));
        Assert.Equal(TimeSpan.Zero, listed.PostedAt.Offset);

        using (var reply = await tester.PostJson("/api/messages", new { parentId = listed.Id, body = "reply" }))
        {
            Assert.Equal(HttpStatusCode.Accepted, reply.StatusCode);
        }

        var replyId = Assert.Single(await sysop.Get<List<PendingMessageDto>>("/api/sysop/queue")).Id;
        using (var approveReply = await sysop.PostJson($"/api/sysop/messages/{replyId}/moderate", new { approve = true }))
        {
            Assert.Equal(2, (await tester.Get<List<MessageDto>>("/api/messages")).Count);
        }

        using (var missing = await sysop.PostJson("/api/sysop/messages/999/moderate", new { approve = true }))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using (var delete = await sysop.Send(HttpMethod.Delete, $"/api/sysop/messages/{listed.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        }

        Assert.Empty(await tester.Get<List<MessageDto>>("/api/messages"));
        using var gone = await sysop.Send(HttpMethod.Delete, $"/api/sysop/messages/{listed.Id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    /// <summary>Bad posts are rejected; the honeypot is silently accepted and dropped; rejected posts stay hidden.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RejectsBadPosts()
    {
        using var tester = await factory.SignInAsync("tester", "Neo");
        using var sysop = await factory.SignInAsync("sysop", "Boss");
        foreach (var body in new object[]
        {
            new { body = "   " },
            new { body = new string('x', Limits.MessageLength + 1) },
            new { body = "cheap VIAGRA here" },
            new { parentId = 12345, body = "reply to nothing" },
        })
        {
            using var response = await tester.PostJson("/api/messages", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var bot = await tester.PostJson("/api/messages", new { body = "spam", website = "http://spam" }))
        {
            Assert.Equal(HttpStatusCode.Accepted, bot.StatusCode);
        }

        using (var botPage = await tester.PostJson("/api/page-sysop", new { message = "spam", website = "x" }))
        {
            Assert.Equal(HttpStatusCode.Accepted, botPage.StatusCode);
        }

        Assert.Empty(await sysop.Get<List<PendingMessageDto>>("/api/sysop/queue"));
        Assert.Empty(await sysop.Get<List<SysopPageDto>>("/api/sysop/pages"));

        using (var post = await tester.PostJson("/api/messages", new { body = "meh" }))
        {
            var id = Assert.Single(await sysop.Get<List<PendingMessageDto>>("/api/sysop/queue")).Id;
            using var reject = await sysop.PostJson($"/api/sysop/messages/{id}/moderate", new { approve = false });
            Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        }

        Assert.Empty(await tester.Get<List<MessageDto>>("/api/messages"));
        Assert.Empty(await sysop.Get<List<PendingMessageDto>>("/api/sysop/queue"));
    }

    /// <summary>Pages reach the SysOp's inbox and phone and can be marked read.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PagesTheSysop()
    {
        using var tester = await factory.SignInAsync("tester", "Neo");
        using var sysop = await factory.SignInAsync("sysop", "Boss");
        using (var page = await tester.PostJson("/api/page-sysop", new { message = "line\none" }))
        {
            Assert.Equal(HttpStatusCode.Accepted, page.StatusCode);
        }

        using (var empty = await tester.PostJson("/api/page-sysop", new { message = "" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        }

        Assert.Contains("PIZON BBS: Neo is paging you|line one", factory.Notifier.Sent);
        var inbox = Assert.Single(await sysop.Get<List<SysopPageDto>>("/api/sysop/pages"));
        Assert.False(inbox.Read);
        using (var read = await sysop.PostJson($"/api/sysop/pages/{inbox.Id}/read"))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        Assert.True(Assert.Single(await sysop.Get<List<SysopPageDto>>("/api/sysop/pages")).Read);
        using var missing = await sysop.PostJson("/api/sysop/pages/999/read");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>The SysOp console is a 404 to everyone else.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SysopApiIsHidden()
    {
        using var guest = factory.Caller();
        using var tester = await factory.SignInAsync("tester", "Neo");
        using var newSysop = await factory.SignInAsync("sysop", null);
        foreach (var client in new[] { guest, tester, newSysop })
        {
            using var response = await client.GetAsync(new Uri("/api/sysop/queue", UriKind.Relative));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    /// <summary>Banning blocks a caller from writing and hides them from the user list.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task BansCallers()
    {
        using var tester = await factory.SignInAsync("tester", "Neo");
        using var sysop = await factory.SignInAsync("sysop", "Boss");
        var ids = await factory.WithDbAsync(db => db.Users.ToDictionaryAsync(u => u.Handle!, u => u.Id));
        Assert.Equal(2, (await tester.Get<List<CallerDto>>("/api/callers/recent")).Count);

        using (var self = await sysop.PostJson($"/api/sysop/users/{ids["Boss"]}/ban"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        }

        using (var missing = await sysop.PostJson("/api/sysop/users/999/ban"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using (var ban = await sysop.PostJson($"/api/sysop/users/{ids["Neo"]}/ban"))
        {
            Assert.Equal(HttpStatusCode.OK, ban.StatusCode);
        }

        using (var post = await tester.PostJson("/api/messages", new { body = "let me in" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
            Assert.Contains("banned", await post.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var handle = await tester.PostJson("/api/me/handle", new { handle = "Neo2" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, handle.StatusCode);
        }

        Assert.Equal("Boss", Assert.Single(await tester.Get<List<CallerDto>>("/api/callers/recent")).Handle);
    }

    /// <summary>Daily-count doors count once per call up to the cap; max-score doors keep the best.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task KeepsScores()
    {
        using var tester = await factory.SignInAsync("tester", "Neo");
        using var sysop = await factory.SignInAsync("sysop", "Boss");
        var day = factory.Clock.Today;

        for (var i = 0; i < 5; i++)
        {
            using var ok = await tester.PostJson("/api/scores/oracle", new ScoreSubmitRequest(1, day));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using (var one = await sysop.PostJson("/api/scores/oracle", new ScoreSubmitRequest(1, day)))
        {
            Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        }

        var board = await tester.Get<List<ScoreEntry>>($"/api/scores/oracle?day={day}");
        Assert.Equal([new ScoreEntry(1, "Neo", 3), new ScoreEntry(2, "Boss", 1)], board);

        foreach (var (url, body, status) in new (string, ScoreSubmitRequest, HttpStatusCode)[]
        {
            ("/api/scores/oracle", new(5, day), HttpStatusCode.BadRequest),
            ("/api/scores/oracle", new(1, "2001-01-01"), HttpStatusCode.BadRequest),
            ("/api/scores/oracle", new(1, "garbage"), HttpStatusCode.BadRequest),
            ("/api/scores/nope", new(1, day), HttpStatusCode.NotFound),
            ("/api/scores/plain", new(1, day), HttpStatusCode.NotFound),
            ("/api/scores/maze", new(101, day), HttpStatusCode.BadRequest),
            ("/api/scores/maze", new(-1, day), HttpStatusCode.BadRequest),
            ("/api/scores/maze", new(50, day), HttpStatusCode.OK),
            ("/api/scores/maze", new(30, "any"), HttpStatusCode.OK),
        })
        {
            using var response = await tester.PostJson(url, body);
            Assert.True(status == response.StatusCode, $"{url} {body}");
        }

        Assert.Equal(50, Assert.Single(await tester.Get<List<ScoreEntry>>("/api/scores/maze")).Score);
        using var badDay = await tester.GetAsync(new Uri("/api/scores/oracle?day=nope", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, badDay.StatusCode);
        using var unknown = await tester.GetAsync(new Uri("/api/scores/nope", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    /// <summary>Logging out clears the cookie.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LogsOut()
    {
        using var tester = await factory.SignInAsync("tester", "Neo");
        using (var logout = await tester.PostJson("/auth/logout"))
        {
            Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        }

        Assert.False((await tester.Get<MeResponse>("/api/me")).Authenticated);
    }

    /// <summary>Too many writes from one address are slowed down.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RateLimitsWrites()
    {
        using var tester = await factory.SignInAsync("tester", "Neo");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 9; i++)
        {
            using var response = await tester.PostJson("/api/me/handle", new { handle = "Neo" });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    /// <summary>Pages, health and the plain-text fallback are served; Discord sign-in is off without settings.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ServesPages()
    {
        using var client = factory.Caller();
        foreach (var (url, status, text) in new[]
        {
            ("/", HttpStatusCode.OK, "boot"),
            ("/", HttpStatusCode.OK, "style=\"--term-glow: 0.1\""),
            ("/healthz", HttpStatusCode.OK, "Healthy"),
            ("/plain", HttpStatusCode.OK, "PIZON BBS (plain text)"),
            ("/Error", HttpStatusCode.OK, "LINE NOISE"),
            ("/no-such-page", HttpStatusCode.NotFound, string.Empty),
        })
        {
            using var response = await client.GetAsync(new Uri(url, UriKind.Relative));
            Assert.True(status == response.StatusCode, url);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(body.Contains(text, StringComparison.Ordinal), $"{url}: {body[..Math.Min(body.Length, 300)]}");
        }

        using var discord = await client.GetAsync(new Uri("/auth/discord?returnUrl=/x", UriKind.Relative));
        Assert.Equal("/?login=failed", discord.Headers.Location!.OriginalString);
        using var unsafeReturn = await client.GetAsync(new Uri("/auth/dev?user=sysop&returnUrl=//evil.example", UriKind.Relative));
        Assert.Equal("/", unsafeReturn.Headers.Location!.OriginalString);
    }
}

/// <summary>
/// Production-only behavior: no dev sign-in, error page in place of the developer page.
/// </summary>
public sealed class ProductionTests : IAsyncDisposable
{
    private readonly BbsFactory factory = new(environment: "Production");

    /// <inheritdoc />
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    /// <summary>The dev sign-in does not exist outside Development, and /api/me says so.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DevSignInIsNotMapped()
    {
        using var client = factory.Caller();
        using var dev = await client.GetAsync(new Uri("/auth/dev?user=sysop", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, dev.StatusCode);
        Assert.Null(dev.Headers.Location);
        Assert.False((await client.Get<MeResponse>("/api/me")).DevLoginEnabled);
        using var health = await client.GetAsync(new Uri("/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, health.StatusCode);
    }
}

/// <summary>
/// The Terminal settings reach the page.
/// </summary>
public sealed class TerminalSettingsTests : IAsyncDisposable
{
    private readonly BbsFactory factory = new(new() { ["Terminal:GlowBlur"] = "0.4" });

    /// <inheritdoc />
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    /// <summary>A configured glow blur is written onto the root element as a CSS variable.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task WritesGlowBlurToThePage()
    {
        using var client = factory.Caller();
        var body = await client.GetStringAsync(new Uri("/", UriKind.Relative));
        Assert.Contains("<html lang=\"en\" style=\"--term-glow: 0.4\">", body, StringComparison.Ordinal);
    }
}

/// <summary>
/// Smoke checks that the database schema matches the model.
/// </summary>
public sealed class DatabaseTests : IAsyncDisposable
{
    private readonly BbsFactory factory = new();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    /// <summary>Migrations are applied at startup and none are pending.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task MigrationsAreCurrent()
    {
        using var client = factory.Caller();
        Assert.Empty(await factory.WithDbAsync(async db => (await db.Database.GetPendingMigrationsAsync()).ToList()));
        Assert.False(await factory.WithDbAsync(db => Task.FromResult(db.Database.HasPendingModelChanges())));
        Assert.Equal(0, await factory.WithDbAsync(db => db.Set<DoorScore>().CountAsync()));
    }
}
