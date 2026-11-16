using Microsoft.EntityFrameworkCore;

namespace Pizon.Web.Data;

/// <summary>
/// The BBS database: callers, messages, SysOp pages and door scores (SQLite).
/// </summary>
/// <param name="options">The context options.</param>
public sealed class BbsDbContext(DbContextOptions<BbsDbContext> options) : DbContext(options)
{
    /// <summary>Gets the signed-in callers.</summary>
    public DbSet<BbsUser> Users => Set<BbsUser>();

    /// <summary>Gets the message board posts.</summary>
    public DbSet<BoardMessage> Messages => Set<BoardMessage>();

    /// <summary>Gets the pages left for the SysOp.</summary>
    public DbSet<SysopPage> SysopPages => Set<SysopPage>();

    /// <summary>Gets the door scores.</summary>
    public DbSet<DoorScore> Scores => Set<DoorScore>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<BbsUser>(e =>
        {
            e.HasIndex(u => u.DiscordId).IsUnique();
            e.HasIndex(u => u.HandleKey).IsUnique();
            e.Property(u => u.DiscordId).HasMaxLength(64);
            e.Property(u => u.Handle).HasMaxLength(16);
            e.Property(u => u.HandleKey).HasMaxLength(16);
            e.Property(u => u.DiscordName).HasMaxLength(64);
        });
        modelBuilder.Entity<BoardMessage>(e =>
        {
            e.HasIndex(m => new { m.Status, m.PostedAt });
            e.Property(m => m.Body).HasMaxLength(500);
            e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId);
        });
        modelBuilder.Entity<SysopPage>(e =>
        {
            e.Property(p => p.Message).HasMaxLength(200);
            e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId);
        });
        modelBuilder.Entity<DoorScore>(e =>
        {
            e.HasIndex(s => new { s.Door, s.UserId, s.Day }).IsUnique();
            e.Property(s => s.Door).HasMaxLength(32);
            e.Property(s => s.Day).HasMaxLength(10);
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId);
        });
    }
}

/// <summary>
/// A caller who signed in with Discord (or the Development sign-in).
/// </summary>
public sealed class BbsUser
{
    /// <summary>Gets or sets the user id.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the Discord user id (or <c>dev-*</c> for Development users).</summary>
    public string DiscordId { get; set; } = string.Empty;

    /// <summary>Gets or sets the Discord display name (or username), used to suggest a handle.</summary>
    public string? DiscordName { get; set; }

    /// <summary>Gets or sets the chosen handle; <see langword="null"/> until the new-user prompt is answered.</summary>
    public string? Handle { get; set; }

    /// <summary>Gets or sets the upper-cased handle, for case-insensitive uniqueness.</summary>
    public string? HandleKey { get; set; }

    /// <summary>Gets or sets when the user first signed in (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets when the user last signed in (UTC).</summary>
    public DateTime LastLoginAt { get; set; }

    /// <summary>Gets or sets a value indicating whether the SysOp banned this user.</summary>
    public bool Banned { get; set; }
}

/// <summary>
/// Where a message is in moderation.
/// </summary>
public enum MessageStatus
{
    /// <summary>Waiting for the SysOp.</summary>
    Pending = 0,

    /// <summary>Visible to everyone.</summary>
    Approved = 1,

    /// <summary>Rejected by the SysOp (kept, never shown).</summary>
    Rejected = 2,
}

/// <summary>
/// A message board post.
/// </summary>
public sealed class BoardMessage
{
    /// <summary>Gets or sets the message id.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the message this replies to, if any.</summary>
    public long? ParentId { get; set; }

    /// <summary>Gets or sets the author's user id.</summary>
    public long UserId { get; set; }

    /// <summary>Gets or sets the author.</summary>
    public BbsUser? User { get; set; }

    /// <summary>Gets or sets the message text.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Gets or sets when it was posted (UTC).</summary>
    public DateTime PostedAt { get; set; }

    /// <summary>Gets or sets the moderation status.</summary>
    public MessageStatus Status { get; set; }
}

/// <summary>
/// A page left for the SysOp.
/// </summary>
public sealed class SysopPage
{
    /// <summary>Gets or sets the page id.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets who paged.</summary>
    public long UserId { get; set; }

    /// <summary>Gets or sets the user who paged.</summary>
    public BbsUser? User { get; set; }

    /// <summary>Gets or sets what they said.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets when (UTC).</summary>
    public DateTime SentAt { get; set; }

    /// <summary>Gets or sets a value indicating whether the SysOp has read it.</summary>
    public bool Read { get; set; }
}

/// <summary>
/// A door score: one row per user per door for "max" boards, and one per user per door per day for
/// "daily count" boards.
/// </summary>
public sealed class DoorScore
{
    /// <summary>Gets or sets the row id.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the door id.</summary>
    public string Door { get; set; } = string.Empty;

    /// <summary>Gets or sets the player's user id.</summary>
    public long UserId { get; set; }

    /// <summary>Gets or sets the player.</summary>
    public BbsUser? User { get; set; }

    /// <summary>Gets or sets the day (yyyy-MM-dd) for daily boards; empty for max boards.</summary>
    public string Day { get; set; } = string.Empty;

    /// <summary>Gets or sets the score.</summary>
    public long Value { get; set; }

    /// <summary>Gets or sets when the score last changed (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}
