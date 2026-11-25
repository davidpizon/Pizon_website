using Pizon.Doors.Sdk;

namespace Pizon.Web.Client.Api;

/// <summary>
/// The PIZON BBS server API, as used by the screens.
/// </summary>
public interface IBbsApi
{
    /// <summary>Gets who the caller is.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The identity; a guest when the server can't be reached.</returns>
    Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default);

    /// <summary>Chooses or changes the caller's handle.</summary>
    /// <param name="handle">The requested handle.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result; <see cref="ApiStatus.Invalid"/> with a reason when taken or not allowed.</returns>
    Task<ApiResult> SetHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>Signs out of Discord.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result.</returns>
    Task<ApiResult> LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists approved messages, oldest first.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The messages.</returns>
    Task<ApiResult<IReadOnlyList<MessageDto>>> GetMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Posts a message (it waits for SysOp approval).</summary>
    /// <param name="request">The message.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result.</returns>
    Task<ApiResult> PostMessageAsync(PostMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Pages the SysOp.</summary>
    /// <param name="request">The page.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result.</returns>
    Task<ApiResult> PageSysopAsync(PageSysopRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists recent signed-in callers.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The callers, most recent first.</returns>
    Task<ApiResult<IReadOnlyList<CallerDto>>> GetRecentCallersAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a door's top-ten board.</summary>
    /// <param name="doorId">The door id.</param>
    /// <param name="day">The caller's local date (daily boards show this day).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The board.</returns>
    Task<ApiResult<IReadOnlyList<ScoreEntry>>> GetScoresAsync(string doorId, string day, CancellationToken cancellationToken = default);

    /// <summary>Submits a door score.</summary>
    /// <param name="doorId">The door id.</param>
    /// <param name="request">The score.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result.</returns>
    Task<ApiResult> SubmitScoreAsync(string doorId, ScoreSubmitRequest request, CancellationToken cancellationToken = default);

    /// <summary>SysOp: lists messages waiting for review.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The queue, oldest first.</returns>
    Task<ApiResult<IReadOnlyList<PendingMessageDto>>> GetModerationQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>SysOp: approves or rejects a message.</summary>
    /// <param name="messageId">The message id.</param>
    /// <param name="approve">Whether to approve.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result.</returns>
    Task<ApiResult> ModerateAsync(long messageId, bool approve, CancellationToken cancellationToken = default);

    /// <summary>SysOp: deletes a message and its replies.</summary>
    /// <param name="messageId">The message id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result.</returns>
    Task<ApiResult> DeleteMessageAsync(long messageId, CancellationToken cancellationToken = default);

    /// <summary>SysOp: lists pages, newest first.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The pages.</returns>
    Task<ApiResult<IReadOnlyList<SysopPageDto>>> GetPagesAsync(CancellationToken cancellationToken = default);

    /// <summary>SysOp: marks a page read.</summary>
    /// <param name="pageId">The page id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result.</returns>
    Task<ApiResult> MarkPageReadAsync(long pageId, CancellationToken cancellationToken = default);

    /// <summary>SysOp: bans a user from posting, paging and scores.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result.</returns>
    Task<ApiResult> BanUserAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Gets a Discord feed's newest posts (anyone, guests included).</summary>
    /// <param name="feed">The feed, for example <see cref="Feeds.Bulletins"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The feed.</returns>
    Task<ApiResult<FeedDto>> GetFeedAsync(string feed, CancellationToken cancellationToken = default);

    /// <summary>Gets one post of a Discord feed by its fixed number.</summary>
    /// <param name="feed">The feed.</param>
    /// <param name="number">The post's number.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The post; <see cref="ApiStatus.Forbidden"/> when there is no such post.</returns>
    Task<ApiResult<FeedPostDto>> GetFeedPostAsync(string feed, int number, CancellationToken cancellationToken = default);
}
