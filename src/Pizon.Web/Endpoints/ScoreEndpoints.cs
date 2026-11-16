using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pizon.Doors.Sdk;
using Pizon.Web.Auth;
using Pizon.Web.Client.Api;
using Pizon.Web.Data;
using Pizon.Web.Services;

namespace Pizon.Web.Endpoints;

/// <summary>
/// Door score boards. "Max" boards keep each player's best; "daily count" boards count one per post per day
/// (the Oracle's Most Devoted Seekers).
/// </summary>
public static class ScoreEndpoints
{
    /// <summary>How many entries a board shows.</summary>
    public const int BoardSize = 10;

    /// <summary>
    /// Maps <c>/api/scores/{door}</c>.
    /// </summary>
    /// <param name="app">The route builder.</param>
    public static void MapScoreEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/scores/{door}", TopAsync);
        app.MapPost("/api/scores/{door}", SubmitAsync)
            .AddEndpointFilter<CsrfHeaderFilter>().AddEndpointFilter<MemberFilter>().RequireRateLimiting(RateLimits.Scores);
    }

    /// <summary>
    /// Parses a caller's day and checks it is within a day of the server's UTC date (time zones differ).
    /// </summary>
    /// <param name="day">The day text (yyyy-MM-dd).</param>
    /// <param name="todayUtc">The server's UTC date.</param>
    /// <returns>The normalized day, or <see langword="null"/>.</returns>
    public static string? ParseDay(string? day, DateOnly todayUtc)
    {
        if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return null;
        }

        return Math.Abs(parsed.DayNumber - todayUtc.DayNumber) <= 1 ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// Gets a door's top ten.
    /// </summary>
    /// <param name="door">The door id.</param>
    /// <param name="day">The caller's day (daily boards).</param>
    /// <param name="catalog">The door catalog.</param>
    /// <param name="db">The database.</param>
    /// <param name="time">The clock.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The board, or 404 for unknown doors.</returns>
    internal static async Task<IResult> TopAsync(string door, string? day, IDoorCatalog catalog, BbsDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var entry = catalog.FindScored(door);
        if (entry is null)
        {
            return Results.NotFound();
        }

        var key = KeyFor(entry, day, time);
        if (key is null)
        {
            return Results.BadRequest("day must be yyyy-MM-dd, today");
        }

        var rows = await db.Scores
            .Where(s => s.Door == entry.Id && s.Day == key && !s.User!.Banned && s.User.Handle != null)
            .OrderByDescending(s => s.Value).ThenBy(s => s.UpdatedAt)
            .Take(BoardSize)
            .Select(s => new { Handle = s.User!.Handle!, s.Value })
            .ToListAsync(cancellationToken);
        return Results.Ok(rows.Select((r, i) => new ScoreEntry(i + 1, r.Handle, r.Value)).ToList());
    }

    /// <summary>
    /// Submits a score.
    /// </summary>
    /// <param name="door">The door id.</param>
    /// <param name="request">The score.</param>
    /// <param name="http">The request.</param>
    /// <param name="catalog">The door catalog.</param>
    /// <param name="db">The database.</param>
    /// <param name="time">The clock.</param>
    /// <returns>200, 400 for impossible scores, 404 for unknown doors.</returns>
    internal static async Task<IResult> SubmitAsync(string door, ScoreSubmitRequest request, HttpContext http, IDoorCatalog catalog, BbsDbContext db, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = CurrentUser.Get(http);
        var entry = catalog.FindScored(door);
        if (entry is null)
        {
            return Results.NotFound();
        }

        var key = KeyFor(entry, request.Day, time);
        if (key is null)
        {
            return Results.BadRequest("day must be yyyy-MM-dd, today");
        }

        var valid = entry.ScoreMode == ScoreMode.DailyCount ? request.Score == 1 : request.Score >= 0 && request.Score <= entry.MaxScore;
        if (!valid)
        {
            return Results.BadRequest("that score is not possible");
        }

        var row = await db.Scores.FirstOrDefaultAsync(s => s.Door == entry.Id && s.UserId == user.Id && s.Day == key, http.RequestAborted);
        if (row is null)
        {
            row = new DoorScore { Door = entry.Id, UserId = user.Id, Day = key };
            db.Scores.Add(row);
        }

        var next = entry.ScoreMode == ScoreMode.DailyCount ? Math.Min(row.Value + 1, entry.MaxScore) : Math.Max(row.Value, request.Score);
        if (next != row.Value || row.Id == 0)
        {
            row.Value = next;
            row.UpdatedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(http.RequestAborted);
        }

        return Results.Ok();
    }

    private static string? KeyFor(DoorManifestEntry entry, string? day, TimeProvider time) =>
        entry.ScoreMode == ScoreMode.DailyCount ? ParseDay(day, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)) : string.Empty;
}
