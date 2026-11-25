namespace Pizon.Web.Services.Discord;

/// <summary>
/// Discord settings for channel feeds (the <c>Discord</c> section, which also holds the sign-in client id and secret).
/// </summary>
public sealed class DiscordFeedOptions
{
    /// <summary>The fewest minutes between checks.</summary>
    public const int MinimumPollMinutes = 1;

    /// <summary>Gets or sets the bot token. Without one, no feed is read.</summary>
    public string? BotToken { get; set; }

    /// <summary>Gets the feeds by name (for example <c>Bulletins</c>), each naming a channel.</summary>
    public Dictionary<string, DiscordFeedChannel> Feeds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets how many minutes pass between checks with Discord.</summary>
    public int PollMinutes { get; set; } = 10;

    /// <summary>
    /// Gets the channel id of a feed that can be read.
    /// </summary>
    /// <param name="feed">The feed name.</param>
    /// <returns>The channel id, or <see langword="null"/> when there is no bot token or no channel for the feed.</returns>
    public string? ChannelFor(string feed) =>
        !string.IsNullOrWhiteSpace(BotToken) && Feeds.TryGetValue(feed, out var channel) && !string.IsNullOrWhiteSpace(channel.ChannelId)
            ? channel.ChannelId.Trim()
            : null;

    /// <summary>
    /// Lists the feeds that can be read, by their lower-case names.
    /// </summary>
    /// <returns>The feed names.</returns>
    public IReadOnlyList<string> EnabledFeeds() =>
        Feeds.Keys.Where(f => ChannelFor(f) is not null).Select(f => f.ToLowerInvariant()).Distinct().Order(StringComparer.Ordinal).ToList();
}

/// <summary>
/// One feed's channel.
/// </summary>
public sealed class DiscordFeedChannel
{
    /// <summary>Gets or sets the Discord channel id.</summary>
    public string? ChannelId { get; set; }
}

/// <summary>
/// The parts of a Discord message object that feeds use.
/// </summary>
public sealed class DiscordMessage
{
    /// <summary>The default message type.</summary>
    public const int DefaultType = 0;

    /// <summary>The reply message type.</summary>
    public const int ReplyType = 19;

    /// <summary>Gets the message id (a snowflake).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the message type; only <see cref="DefaultType"/> and <see cref="ReplyType"/> carry text people wrote.</summary>
    public int Type { get; init; }

    /// <summary>Gets the text (Discord markdown).</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Gets when it was posted.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets when it was last edited, if ever.</summary>
    public DateTimeOffset? EditedTimestamp { get; init; }

    /// <summary>Gets the author.</summary>
    public DiscordUser Author { get; init; } = new();

    /// <summary>Gets the users mentioned in the text.</summary>
    public IReadOnlyList<DiscordUser> Mentions { get; init; } = [];

    /// <summary>Gets the attached files.</summary>
    public IReadOnlyList<DiscordAttachment> Attachments { get; init; } = [];

    /// <summary>Gets the embeds (link previews and rich cards).</summary>
    public IReadOnlyList<DiscordEmbed> Embeds { get; init; } = [];

    /// <summary>Gets a value indicating whether the message is something a person wrote (not a join, pin or other system message).</summary>
    public bool IsText => Type is DefaultType or ReplyType;

    /// <summary>Gets the id as a number, for ordering; 0 when it is not a snowflake.</summary>
    public ulong Snowflake => ulong.TryParse(Id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : 0;
}

/// <summary>
/// A Discord user.
/// </summary>
public sealed class DiscordUser
{
    /// <summary>Gets the user id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the username.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Gets the display name, if set.</summary>
    public string? GlobalName { get; init; }

    /// <summary>Gets the best name to show: the display name, then the username.</summary>
    public string Name => string.IsNullOrWhiteSpace(GlobalName) ? Username : GlobalName;
}

/// <summary>
/// A file attached to a Discord message.
/// </summary>
public sealed class DiscordAttachment
{
    /// <summary>Gets the file name.</summary>
    public string Filename { get; init; } = string.Empty;

    /// <summary>Gets the media type, if Discord knows it.</summary>
    public string? ContentType { get; init; }

    /// <summary>Gets a value indicating whether the file is a picture.</summary>
    public bool IsImage => ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// A Discord embed.
/// </summary>
public sealed class DiscordEmbed
{
    /// <summary>Gets the title.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the description (Discord markdown).</summary>
    public string? Description { get; init; }

    /// <summary>Gets the link.</summary>
    public string? Url { get; init; }
}

/// <summary>
/// Discord could not be read: a network failure, a refused token, a missing channel or a rate limit.
/// </summary>
public sealed class DiscordUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordUnavailableException"/> class.
    /// </summary>
    public DiscordUnavailableException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordUnavailableException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public DiscordUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordUnavailableException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public DiscordUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
