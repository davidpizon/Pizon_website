using System.Text.RegularExpressions;

namespace Pizon.Web.Client.Api;

/// <summary>
/// The caller's identity as seen by the server (<c>GET /api/me</c>).
/// </summary>
/// <param name="Authenticated">Whether the caller signed in with Discord (or the dev sign-in).</param>
/// <param name="Handle">The chosen BBS handle; <see langword="null"/> for guests and new users.</param>
/// <param name="SuggestedHandle">The Discord display name or username, offered on the new-user prompt.</param>
/// <param name="Level">The security level: 0 guest, 10 user, 255 SysOp.</param>
/// <param name="IsSysop">Whether the caller is the SysOp.</param>
/// <param name="DiscordEnabled">Whether Discord sign-in is configured on this server.</param>
/// <param name="DevLoginEnabled">Whether the Development-only fake sign-in is available.</param>
public sealed record MeResponse(
    bool Authenticated,
    string? Handle,
    string? SuggestedHandle,
    int Level,
    bool IsSysop,
    bool DiscordEnabled,
    bool DevLoginEnabled)
{
    /// <summary>Gets a guest identity, used before the server answers or when it is unreachable.</summary>
    public static MeResponse Guest { get; } = new(false, null, null, SecurityLevels.Guest, false, false, false);
}

/// <summary>
/// The security levels shown in the status header.
/// </summary>
public static class SecurityLevels
{
    /// <summary>A guest caller.</summary>
    public const int Guest = 0;

    /// <summary>A signed-in caller.</summary>
    public const int User = 10;

    /// <summary>The SysOp.</summary>
    public const int Sysop = 255;
}

/// <summary>
/// Request body for choosing or changing a handle.
/// </summary>
/// <param name="Handle">The requested handle.</param>
public sealed record SetHandleRequest(string Handle);

/// <summary>
/// A message on the board (approved messages only, unless returned to the SysOp).
/// </summary>
/// <param name="Id">The message id.</param>
/// <param name="ParentId">The message this replies to, if any.</param>
/// <param name="Handle">The author's handle.</param>
/// <param name="Body">The message text.</param>
/// <param name="PostedAt">When it was posted.</param>
public sealed record MessageDto(long Id, long? ParentId, string Handle, string Body, DateTimeOffset PostedAt);

/// <summary>
/// Request body for posting a message.
/// </summary>
/// <param name="ParentId">The message being replied to, or <see langword="null"/> for a new thread.</param>
/// <param name="Body">The message text (at most <see cref="Limits.MessageLength"/> characters).</param>
/// <param name="Website">A honeypot field: humans never fill it in.</param>
public sealed record PostMessageRequest(long? ParentId, string Body, string? Website = null);

/// <summary>
/// Request body for paging the SysOp.
/// </summary>
/// <param name="Message">What the caller wants to say.</param>
/// <param name="Website">A honeypot field: humans never fill it in.</param>
public sealed record PageSysopRequest(string Message, string? Website = null);

/// <summary>
/// A recent caller in the user list.
/// </summary>
/// <param name="Handle">The caller's handle.</param>
/// <param name="LastSeen">When they last signed in.</param>
/// <param name="Level">Their security level.</param>
public sealed record CallerDto(string Handle, DateTimeOffset LastSeen, int Level);

/// <summary>
/// Request body for submitting a door score.
/// </summary>
/// <param name="Score">The score, or the increment for daily-count doors.</param>
/// <param name="Day">The caller's local date (yyyy-MM-dd), used by daily boards.</param>
public sealed record ScoreSubmitRequest(long Score, string Day);

/// <summary>
/// A message waiting for SysOp review.
/// </summary>
/// <param name="Id">The message id.</param>
/// <param name="ParentId">The parent message, if a reply.</param>
/// <param name="Handle">The author's handle.</param>
/// <param name="UserId">The author's user id (for banning).</param>
/// <param name="Body">The message text.</param>
/// <param name="PostedAt">When it was posted.</param>
public sealed record PendingMessageDto(long Id, long? ParentId, string Handle, long UserId, string Body, DateTimeOffset PostedAt);

/// <summary>
/// A page left for the SysOp.
/// </summary>
/// <param name="Id">The page id.</param>
/// <param name="Handle">Who paged.</param>
/// <param name="Message">What they said.</param>
/// <param name="SentAt">When.</param>
/// <param name="Read">Whether the SysOp has read it.</param>
public sealed record SysopPageDto(long Id, string Handle, string Message, DateTimeOffset SentAt, bool Read);

/// <summary>
/// Request body for approving or rejecting a pending message.
/// </summary>
/// <param name="Approve"><see langword="true"/> to approve, <see langword="false"/> to reject.</param>
public sealed record ModerateRequest(bool Approve);

/// <summary>
/// Shared input limits, enforced on both client and server.
/// </summary>
public static partial class Limits
{
    /// <summary>The longest message body.</summary>
    public const int MessageLength = 500;

    /// <summary>The longest Page SysOp message.</summary>
    public const int PageLength = 200;

    /// <summary>The shortest handle.</summary>
    public const int HandleMin = 3;

    /// <summary>The longest handle.</summary>
    public const int HandleMax = 16;

    /// <summary>
    /// Checks a handle's shape: 3-16 letters, digits, underscores or hyphens.
    /// </summary>
    /// <param name="handle">The handle to check.</param>
    /// <returns><see langword="true"/> when the handle is well formed.</returns>
    public static bool IsValidHandleShape(string? handle) => handle is not null && HandleRegex().IsMatch(handle);

    /// <summary>
    /// Turns a Discord name into a handle suggestion by dropping unsupported characters and trimming the length.
    /// </summary>
    /// <param name="name">A Discord display name or username.</param>
    /// <returns>A suggestion that satisfies <see cref="IsValidHandleShape"/>, or <see langword="null"/>.</returns>
    public static string? SuggestHandle(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var cleaned = new string(name.Replace(' ', '_').Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').ToArray());
        if (cleaned.Length > HandleMax)
        {
            cleaned = cleaned[..HandleMax];
        }

        return IsValidHandleShape(cleaned) ? cleaned : null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{3,16}$", RegexOptions.CultureInvariant)]
    private static partial Regex HandleRegex();
}

/// <summary>
/// The Discord channel feeds the server can relay (each one a channel named in the <c>Discord:Feeds</c> settings).
/// </summary>
public static class Feeds
{
    /// <summary>The feed shown as SYSTEM BULLETINS.</summary>
    public const string Bulletins = "bulletins";

    /// <summary>How many posts a feed listing returns.</summary>
    public const int ListSize = 10;
}

/// <summary>
/// One Discord message relayed by a feed.
/// </summary>
/// <param name="Number">The post's fixed number in the feed: the first post ever is 1.</param>
/// <param name="Title">The title (the message's first line), as pipe-code text.</param>
/// <param name="Lines">The body lines, as pipe-code text.</param>
/// <param name="Author">Who posted it, as pipe-code text.</param>
/// <param name="PostedAt">When it was posted.</param>
/// <param name="EditedAt">When it was last edited, if ever.</param>
public sealed record FeedPostDto(int Number, string Title, IReadOnlyList<string> Lines, string Author, DateTimeOffset PostedAt, DateTimeOffset? EditedAt);

/// <summary>
/// A feed's newest posts (<c>GET /api/feeds/{feed}</c>).
/// </summary>
/// <param name="Enabled">Whether the feed is configured on this server.</param>
/// <param name="Online">Whether the last check with Discord worked (saved posts are returned either way).</param>
/// <param name="Posts">The newest posts, newest first.</param>
public sealed record FeedDto(bool Enabled, bool Online, IReadOnlyList<FeedPostDto> Posts)
{
    /// <summary>Gets an empty, disabled feed: what screens use when the server can't be reached.</summary>
    public static FeedDto None { get; } = new(false, true, []);
}
