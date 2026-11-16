using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pizon.Web.Client.Api;
using Pizon.Web.Data;

namespace Pizon.Web.Auth;

/// <summary>
/// Options naming the SysOp (<c>SysOp</c> section).
/// </summary>
public sealed class SysopOptions
{
    /// <summary>Gets or sets the SysOp's Discord user id. Whoever signs in with it gets level 255.</summary>
    public string? DiscordUserId { get; set; }
}

/// <summary>
/// Claim types written into the sign-in cookie.
/// </summary>
public static class BbsClaims
{
    /// <summary>The BBS user id.</summary>
    public const string UserId = "pizon:uid";

    /// <summary>The Discord id prefix used by the Development sign-in.</summary>
    public const string DevPrefix = "dev-";

    /// <summary>
    /// Builds the cookie principal for a user.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="authenticationType">The authentication type.</param>
    /// <returns>The principal.</returns>
    public static ClaimsPrincipal PrincipalFor(BbsUser user, string authenticationType)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = new ClaimsIdentity(
            [new Claim(UserId, user.Id.ToString(CultureInfo.InvariantCulture))],
            authenticationType);
        return new ClaimsPrincipal(identity);
    }
}

/// <summary>
/// Finds and records callers: who is signed in, whether they are the SysOp, and sign-in upserts.
/// </summary>
/// <param name="db">The database.</param>
/// <param name="sysop">The SysOp options.</param>
/// <param name="environment">The host environment (the dev SysOp exists only in Development).</param>
/// <param name="time">The clock.</param>
public sealed class CallerDirectory(BbsDbContext db, IOptions<SysopOptions> sysop, IHostEnvironment environment, TimeProvider time)
{
    /// <summary>The Development SysOp's fake Discord id.</summary>
    public const string DevSysopId = BbsClaims.DevPrefix + "sysop";

    /// <summary>
    /// Gets the signed-in user for a request.
    /// </summary>
    /// <param name="principal">The request's user.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The user, or <see langword="null"/> for guests (or a user that no longer exists).</returns>
    public async Task<BbsUser?> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var claim = principal.FindFirst(BbsClaims.UserId)?.Value;
        if (!long.TryParse(claim, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        return await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    /// <summary>
    /// Checks whether a user is the SysOp.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns><see langword="true"/> for the configured SysOp (or the Development SysOp).</returns>
    public bool IsSysop(BbsUser? user)
    {
        if (user is null || user.Banned)
        {
            return false;
        }

        var configured = sysop.Value.DiscordUserId;
        return (!string.IsNullOrWhiteSpace(configured) && string.Equals(user.DiscordId, configured.Trim(), StringComparison.Ordinal)) ||
            (environment.IsDevelopment() && user.DiscordId == DevSysopId);
    }

    /// <summary>
    /// Gets the security level for a user.
    /// </summary>
    /// <param name="user">The user, or <see langword="null"/> for a guest.</param>
    /// <returns>0, 10 or 255.</returns>
    public int LevelOf(BbsUser? user) =>
        user?.Handle is null ? SecurityLevels.Guest : IsSysop(user) ? SecurityLevels.Sysop : SecurityLevels.User;

    /// <summary>
    /// Records a sign-in: creates the user on first sign-in and refreshes the name and last-login time.
    /// </summary>
    /// <param name="discordId">The Discord user id.</param>
    /// <param name="discordName">The Discord display name (or username).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The user.</returns>
    public async Task<BbsUser> SignedInAsync(string discordId, string? discordName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discordId);
        var now = time.GetUtcNow().UtcDateTime;
        var user = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == discordId, cancellationToken);
        if (user is null)
        {
            user = new BbsUser { DiscordId = discordId, CreatedAt = now };
            db.Users.Add(user);
        }

        user.DiscordName = discordName is { Length: > 64 } ? discordName[..64] : discordName;
        user.LastLoginAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }
}
