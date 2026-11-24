using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Data;

namespace Pizon.Web.Services.Discord;

/// <summary>
/// Copies Discord channel messages into the database every <see cref="DiscordFeedOptions.PollMinutes"/>, so
/// feeds keep working while Discord is down. New messages get the next number in their feed (the first ever
/// is 1). The newest posts also follow edits and deletes; a deleted post keeps its number.
/// </summary>
/// <param name="scopes">Creates a database scope per check.</param>
/// <param name="discord">The Discord client.</param>
/// <param name="options">The feed options.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class FeedSync(
    IServiceScopeFactory scopes,
    DiscordClient discord,
    IOptions<DiscordFeedOptions> options,
    TimeProvider time,
    ILogger<FeedSync> logger) : BackgroundService
{
    /// <summary>The most pages of history read in one check (the rest waits for the next one).</summary>
    public const int MaxPagesPerSync = 50;

    /// <summary>How many of the newest messages are compared for edits and deletes.</summary>
    public const int RecheckSize = 50;

    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// Checks one feed now.
    /// </summary>
    /// <param name="feed">The feed name.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> when Discord was read; <see langword="false"/> when it failed or the feed is not configured.</returns>
    public async Task<bool> SyncAsync(string feed, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feed);
        var channel = options.Value.ChannelFor(feed);
        if (channel is null)
        {
            return false;
        }

        var key = feed.ToLowerInvariant();
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BbsDbContext>();
            bool ok;
            try
            {
                var (added, read) = await CopyNewAsync(db, key, channel, cancellationToken);
                var newest = await RecheckAsync(db, key, channel, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                LogSynced(logger, key, added, read, newest);
                ok = true;
            }
            catch (DiscordUnavailableException ex)
            {
                LogFailed(logger, key, ex);
                db.ChangeTracker.Clear();
                ok = false;
            }

            var state = await db.FeedStates.FindAsync([key], cancellationToken);
            if (state is null)
            {
                state = new FeedState { Feed = key };
                db.FeedStates.Add(state);
            }

            state.LastSyncAt = time.GetUtcNow().UtcDateTime;
            state.LastSyncOk = ok;
            await db.SaveChangesAsync(cancellationToken);
            return ok;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        gate.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var feeds = options.Value.EnabledFeeds();
        if (feeds.Count == 0)
        {
            return;
        }

        var minutes = Math.Max(DiscordFeedOptions.MinimumPollMinutes, options.Value.PollMinutes);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes), time);
        do
        {
            foreach (var feed in feeds)
            {
                await SyncAsync(feed, stoppingToken);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static DateTime Utc(DateTimeOffset when) => when.UtcDateTime;

    private static void Apply(FeedPost post, DiscordMessage message)
    {
        var (title, lines) = DiscordText.Convert(message);
        post.Title = title;
        post.Body = string.Join('\n', lines);
        post.Author = PipeCodes.Escape(message.Author.Name);
        post.PostedAt = Utc(message.Timestamp);
        post.EditedAt = message.EditedTimestamp is { } edited ? Utc(edited) : null;
    }

    private async Task<(int Added, int Read)> CopyNewAsync(BbsDbContext db, string feed, string channel, CancellationToken cancellationToken)
    {
        var known = await db.FeedPosts.Where(p => p.Feed == feed).Select(p => new { p.MessageId, p.Number }).ToListAsync(cancellationToken);
        var after = known.Count == 0 ? "0" : known.MaxBy(p => Snowflake(p.MessageId))!.MessageId;
        var next = known.Count == 0 ? 1 : known.Max(p => p.Number) + 1;
        var added = 0;
        var read = 0;
        for (var page = 0; page < MaxPagesPerSync; page++)
        {
            var messages = await discord.GetMessagesAsync(channel, after, DiscordClient.PageSize, cancellationToken);
            if (messages.Count == 0)
            {
                break;
            }

            read += messages.Count;
            foreach (var skipped in messages.Where(m => !m.IsText))
            {
                LogSkipped(logger, feed, skipped.Id, skipped.Type);
            }

            foreach (var message in messages.Where(m => m.IsText && m.Snowflake > Snowflake(after)).OrderBy(m => m.Snowflake))
            {
                var post = new FeedPost { Feed = feed, MessageId = message.Id, Number = next++ };
                Apply(post, message);
                db.FeedPosts.Add(post);
                added++;
            }

            after = messages.MaxBy(m => m.Snowflake)!.Id;
            if (messages.Count < DiscordClient.PageSize)
            {
                break;
            }
        }

        return (added, read);
    }

    private async Task<int> RecheckAsync(BbsDbContext db, string feed, string channel, CancellationToken cancellationToken)
    {
        var newest = await discord.GetMessagesAsync(channel, null, RecheckSize, cancellationToken);
        var byId = newest.Where(m => m.IsText).ToDictionary(m => m.Id, StringComparer.Ordinal);

        // Discord returned everything newer than its oldest message here; with fewer than asked, that is the whole channel.
        var oldest = newest.Count < RecheckSize ? 0 : newest.Min(m => m.Snowflake);
        var stored = await db.FeedPosts
            .Where(p => p.Feed == feed && !p.Deleted)
            .OrderByDescending(p => p.Number)
            .Take(RecheckSize)
            .ToListAsync(cancellationToken);
        foreach (var post in stored.Where(p => Snowflake(p.MessageId) >= oldest))
        {
            if (!byId.TryGetValue(post.MessageId, out var message))
            {
                post.Deleted = true;
            }
            else
            {
                // Refreshed every time, not only on edits: Discord sends empty text until Message Content Intent
                // is on, and the conversion can change between versions. Only changed columns are written.
                Apply(post, message);
            }
        }

        return newest.Count;
    }

    private static ulong Snowflake(string id) => new DiscordMessage { Id = id }.Snowflake;

    [LoggerMessage(Level = LogLevel.Information, Message = "Discord feed {Feed} skipped message {Id} of type {Type} (not a written message)")]
    private static partial void LogSkipped(ILogger logger, string feed, string id, int type);

    [LoggerMessage(Level = LogLevel.Information, Message = "Discord feed {Feed} checked: {Added} new posts from {Read} new messages; {Newest} newest messages rechecked")]
    private static partial void LogSynced(ILogger logger, string feed, int added, int read, int newest);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discord feed {Feed} could not be checked")]
    private static partial void LogFailed(ILogger logger, string feed, Exception exception);
}
