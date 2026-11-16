using Microsoft.EntityFrameworkCore;
using Pizon.Web.Auth;
using Pizon.Web.Client.Api;
using Pizon.Web.Data;
using Pizon.Web.Services;

namespace Pizon.Web.Endpoints;

/// <summary>
/// The public board API: messages (approve-first), Page SysOp and the recent callers list.
/// </summary>
public static class BoardEndpoints
{
    /// <summary>The most messages listed.</summary>
    public const int MaxListed = 200;

    /// <summary>
    /// Maps <c>/api/messages</c>, <c>/api/page-sysop</c> and <c>/api/callers/recent</c>.
    /// </summary>
    /// <param name="app">The route builder.</param>
    public static void MapBoardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/messages", ListMessagesAsync);
        app.MapPost("/api/messages", PostMessageAsync)
            .AddEndpointFilter<CsrfHeaderFilter>().AddEndpointFilter<MemberFilter>().RequireRateLimiting(RateLimits.Writes);
        app.MapPost("/api/page-sysop", PageSysopAsync)
            .AddEndpointFilter<CsrfHeaderFilter>().AddEndpointFilter<MemberFilter>().RequireRateLimiting(RateLimits.Writes);
        app.MapGet("/api/callers/recent", RecentCallersAsync);
    }

    /// <summary>
    /// Lists approved messages, oldest first.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The messages.</returns>
    internal static async Task<List<MessageDto>> ListMessagesAsync(BbsDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Messages
            .Where(m => m.Status == MessageStatus.Approved)
            .OrderByDescending(m => m.PostedAt)
            .Take(MaxListed)
            .Select(m => new { m.Id, m.ParentId, Handle = m.User!.Handle ?? "?", m.Body, m.PostedAt })
            .ToListAsync(cancellationToken);
        return rows.OrderBy(m => m.PostedAt).Select(m => new MessageDto(m.Id, m.ParentId, m.Handle, m.Body, Utc(m.PostedAt))).ToList();
    }

    /// <summary>
    /// Posts a message for review.
    /// </summary>
    /// <param name="request">The message.</param>
    /// <param name="http">The request.</param>
    /// <param name="db">The database.</param>
    /// <param name="filter">The word filter.</param>
    /// <param name="notifier">The SysOp notifier.</param>
    /// <param name="time">The clock.</param>
    /// <returns>202 when queued, 400 with a reason.</returns>
    internal static async Task<IResult> PostMessageAsync(PostMessageRequest request, HttpContext http, BbsDbContext db, WordFilter filter, INotifier notifier, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = CurrentUser.Get(http);
        if (!string.IsNullOrEmpty(request.Website))
        {
            return Results.Accepted();
        }

        var body = TextRules.Clean(request.Body, allowNewLines: true);
        if (TextRules.Validate(body, Limits.MessageLength, filter) is { } reason)
        {
            return Results.BadRequest(reason);
        }

        if (request.ParentId is { } parent && !await db.Messages.AnyAsync(m => m.Id == parent && m.Status == MessageStatus.Approved, http.RequestAborted))
        {
            return Results.BadRequest("no such message to reply to");
        }

        db.Messages.Add(new BoardMessage
        {
            ParentId = request.ParentId,
            UserId = user.Id,
            Body = body,
            PostedAt = time.GetUtcNow().UtcDateTime,
            Status = MessageStatus.Pending,
        });
        await db.SaveChangesAsync(http.RequestAborted);
        await notifier.NotifyAsync("PIZON BBS: message waiting for review", $"{user.Handle}: {Preview(body)}", http.RequestAborted);
        return Results.Accepted();
    }

    /// <summary>
    /// Pages the SysOp.
    /// </summary>
    /// <param name="request">The page.</param>
    /// <param name="http">The request.</param>
    /// <param name="db">The database.</param>
    /// <param name="filter">The word filter.</param>
    /// <param name="notifier">The SysOp notifier.</param>
    /// <param name="time">The clock.</param>
    /// <returns>202 when sent, 400 with a reason.</returns>
    internal static async Task<IResult> PageSysopAsync(PageSysopRequest request, HttpContext http, BbsDbContext db, WordFilter filter, INotifier notifier, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = CurrentUser.Get(http);
        if (!string.IsNullOrEmpty(request.Website))
        {
            return Results.Accepted();
        }

        var text = TextRules.Clean(request.Message, allowNewLines: false);
        if (TextRules.Validate(text, Limits.PageLength, filter) is { } reason)
        {
            return Results.BadRequest(reason);
        }

        db.SysopPages.Add(new SysopPage { UserId = user.Id, Message = text, SentAt = time.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(http.RequestAborted);
        await notifier.NotifyAsync($"PIZON BBS: {user.Handle} is paging you", text, http.RequestAborted);
        return Results.Accepted();
    }

    /// <summary>
    /// Lists the 20 most recent signed-in callers with handles (never guests, never banned users).
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="callers">The caller directory (for levels).</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The callers, most recent first.</returns>
    internal static async Task<List<CallerDto>> RecentCallersAsync(BbsDbContext db, CallerDirectory callers, CancellationToken cancellationToken)
    {
        var users = await db.Users
            .Where(u => u.Handle != null && !u.Banned)
            .OrderByDescending(u => u.LastLoginAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        return users.Select(u => new CallerDto(u.Handle!, Utc(u.LastLoginAt), callers.LevelOf(u))).ToList();
    }

    /// <summary>
    /// Shortens text for a notification.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>At most 120 characters on one line.</returns>
    internal static string Preview(string text)
    {
        var flat = text.Replace('\n', ' ');
        return flat.Length <= 120 ? flat : flat[..117] + "...";
    }

    /// <summary>
    /// Marks a stored UTC time as UTC.
    /// </summary>
    /// <param name="value">A UTC time from the database.</param>
    /// <returns>The time with a zero offset.</returns>
    internal static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
