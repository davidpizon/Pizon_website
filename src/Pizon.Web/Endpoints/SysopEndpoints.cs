using Microsoft.EntityFrameworkCore;
using Pizon.Web.Auth;
using Pizon.Web.Client.Api;
using Pizon.Web.Data;

namespace Pizon.Web.Endpoints;

/// <summary>
/// The SysOp console API: moderation queue, approve/reject, delete, pages inbox and bans. Everyone else
/// gets 404.
/// </summary>
public static class SysopEndpoints
{
    /// <summary>
    /// Maps <c>/api/sysop/*</c>.
    /// </summary>
    /// <param name="app">The route builder.</param>
    public static void MapSysopEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sysop").AddEndpointFilter<SysopFilter>().AddEndpointFilter<CsrfHeaderFilter>();
        group.MapGet("/queue", QueueAsync);
        group.MapPost("/messages/{id:long}/moderate", ModerateAsync);
        group.MapDelete("/messages/{id:long}", DeleteAsync);
        group.MapGet("/pages", PagesAsync);
        group.MapPost("/pages/{id:long}/read", MarkReadAsync);
        group.MapPost("/users/{id:long}/ban", BanAsync);
    }

    /// <summary>
    /// Lists pending messages, oldest first.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The queue.</returns>
    internal static async Task<List<PendingMessageDto>> QueueAsync(BbsDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Messages
            .Where(m => m.Status == MessageStatus.Pending)
            .OrderBy(m => m.PostedAt)
            .Select(m => new { m.Id, m.ParentId, Handle = m.User!.Handle ?? "?", m.UserId, m.Body, m.PostedAt })
            .ToListAsync(cancellationToken);
        return rows.Select(m => new PendingMessageDto(m.Id, m.ParentId, m.Handle, m.UserId, m.Body, BoardEndpoints.Utc(m.PostedAt))).ToList();
    }

    /// <summary>
    /// Approves or rejects a message.
    /// </summary>
    /// <param name="id">The message id.</param>
    /// <param name="request">Approve or reject.</param>
    /// <param name="db">The database.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>200 or 404.</returns>
    internal static async Task<IResult> ModerateAsync(long id, ModerateRequest request, BbsDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (message is null)
        {
            return Results.NotFound();
        }

        message.Status = request.Approve ? MessageStatus.Approved : MessageStatus.Rejected;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok();
    }

    /// <summary>
    /// Deletes a message and every reply under it.
    /// </summary>
    /// <param name="id">The message id.</param>
    /// <param name="db">The database.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>200 or 404.</returns>
    internal static async Task<IResult> DeleteAsync(long id, BbsDbContext db, CancellationToken cancellationToken)
    {
        var all = await db.Messages.Select(m => new { m.Id, m.ParentId }).ToListAsync(cancellationToken);
        if (all.All(m => m.Id != id))
        {
            return Results.NotFound();
        }

        var doomed = new HashSet<long> { id };
        var added = true;
        while (added)
        {
            added = false;
            foreach (var m in all.Where(m => m.ParentId is { } p && doomed.Contains(p) && !doomed.Contains(m.Id)))
            {
                added |= doomed.Add(m.Id);
            }
        }

        await db.Messages.Where(m => doomed.Contains(m.Id)).ExecuteDeleteAsync(cancellationToken);
        return Results.Ok();
    }

    /// <summary>
    /// Lists pages, newest first.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The pages.</returns>
    internal static async Task<List<SysopPageDto>> PagesAsync(BbsDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.SysopPages
            .OrderByDescending(p => p.SentAt)
            .Take(100)
            .Select(p => new { p.Id, Handle = p.User!.Handle ?? "?", p.Message, p.SentAt, p.Read })
            .ToListAsync(cancellationToken);
        return rows.Select(p => new SysopPageDto(p.Id, p.Handle, p.Message, BoardEndpoints.Utc(p.SentAt), p.Read)).ToList();
    }

    /// <summary>
    /// Marks a page read.
    /// </summary>
    /// <param name="id">The page id.</param>
    /// <param name="db">The database.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>200 or 404.</returns>
    internal static async Task<IResult> MarkReadAsync(long id, BbsDbContext db, CancellationToken cancellationToken)
    {
        var count = await db.SysopPages.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Read, true), cancellationToken);
        return count == 0 ? Results.NotFound() : Results.Ok();
    }

    /// <summary>
    /// Bans a user from posting, paging and scores. The SysOp can't ban themself.
    /// </summary>
    /// <param name="id">The user id.</param>
    /// <param name="http">The request.</param>
    /// <param name="db">The database.</param>
    /// <returns>200, 400 or 404.</returns>
    internal static async Task<IResult> BanAsync(long id, HttpContext http, BbsDbContext db)
    {
        if (CurrentUser.Get(http).Id == id)
        {
            return Results.BadRequest("the SysOp cannot ban the SysOp");
        }

        var count = await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Banned, true), http.RequestAborted);
        return count == 0 ? Results.NotFound() : Results.Ok();
    }
}
