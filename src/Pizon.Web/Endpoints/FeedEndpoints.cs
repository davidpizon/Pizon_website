using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pizon.Web.Client.Api;
using Pizon.Web.Data;
using Pizon.Web.Services.Discord;

namespace Pizon.Web.Endpoints;

/// <summary>
/// The Discord feeds: the saved copies of channel messages, readable by anyone (guests included).
/// </summary>
public static class FeedEndpoints
{
    /// <summary>The longest feed name.</summary>
    public const int MaxFeedLength = 32;

    /// <summary>
    /// Maps <c>/api/feeds/{feed}</c> and <c>/api/feeds/{feed}/{number}</c>.
    /// </summary>
    /// <param name="app">The route builder.</param>
    public static void MapFeedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/feeds/{feed}", ListAsync);
        app.MapGet("/api/feeds/{feed}/{number:int}", GetAsync);
    }

    /// <summary>
    /// Reads a feed's newest posts.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="options">The feed options.</param>
    /// <param name="feed">The feed name.</param>
    /// <param name="count">How many posts.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The feed.</returns>
    public static async Task<FeedDto> ReadAsync(BbsDbContext db, DiscordFeedOptions options, string feed, int count, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(feed);
        var key = feed.ToLowerInvariant();
        var posts = await db.FeedPosts
            .Where(p => p.Feed == key && !p.Deleted)
            .OrderByDescending(p => p.Number)
            .Take(count)
            .ToListAsync(cancellationToken);
        var state = await db.FeedStates.AsNoTracking().FirstOrDefaultAsync(s => s.Feed == key, cancellationToken);
        return new FeedDto(options.ChannelFor(key) is not null, state?.LastSyncOk ?? true, posts.Select(ToDto).ToList());
    }

    /// <summary>
    /// Turns a saved post into its API shape.
    /// </summary>
    /// <param name="post">The post.</param>
    /// <returns>The DTO.</returns>
    public static FeedPostDto ToDto(FeedPost post)
    {
        ArgumentNullException.ThrowIfNull(post);
        var lines = post.Body.Length == 0 ? [] : post.Body.Split('\n');
        return new FeedPostDto(post.Number, post.Title, lines, post.Author, AsUtc(post.PostedAt), post.EditedAt is { } edited ? AsUtc(edited) : null);
    }

    /// <summary>
    /// Gets a feed's newest posts.
    /// </summary>
    /// <param name="feed">The feed name.</param>
    /// <param name="db">The database.</param>
    /// <param name="options">The feed options.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The feed, or 404 for a name that can't be a feed.</returns>
    internal static async Task<IResult> ListAsync(string feed, BbsDbContext db, IOptions<DiscordFeedOptions> options, CancellationToken cancellationToken)
    {
        if (feed.Length > MaxFeedLength)
        {
            return Results.NotFound();
        }

        return Results.Ok(await ReadAsync(db, options.Value, feed, Feeds.ListSize, cancellationToken));
    }

    /// <summary>
    /// Gets one post by its number.
    /// </summary>
    /// <param name="feed">The feed name.</param>
    /// <param name="number">The post's number.</param>
    /// <param name="db">The database.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The post, or 404.</returns>
    internal static async Task<IResult> GetAsync(string feed, int number, BbsDbContext db, CancellationToken cancellationToken)
    {
        var key = feed.ToLowerInvariant();
        var post = await db.FeedPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Feed == key && p.Number == number && !p.Deleted, cancellationToken);
        return post is null ? Results.NotFound() : Results.Ok(ToDto(post));
    }

    private static DateTimeOffset AsUtc(DateTime when) => new(DateTime.SpecifyKind(when, DateTimeKind.Utc));
}
