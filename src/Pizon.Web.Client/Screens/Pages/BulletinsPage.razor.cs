using System.Globalization;
using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Screens.Art;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// [1] System Bulletins (<c>/bulletins</c>): the newest posts from the Discord bulletins channel first (keys 1-9
/// and 0, each a link to <c>/bulletins/d/{n}</c>), then the SysOp's file bulletins (letter keys, each a link to
/// <c>/bulletins/{n}</c>), plus the Top Players bulletin at <c>/bulletins/top</c>. When the last check with
/// Discord failed, a line says the relay is offline and the saved posts still show.
/// </summary>
public sealed partial class BulletinsPage
{
    /// <summary>The line shown when the last check with Discord failed.</summary>
    public const string OfflineLine = "|08  -- Discord relay offline: showing the last saved posts --|07";

    /// <summary>
    /// The longest Discord title shown in the list, in visible characters: with "[1] Bulletin #D999: " before it
    /// and "  (11/24/26 07:40)" after it, the line fits the 76-column list.
    /// </summary>
    public const int ListTitleWidth = 38;

    /// <summary>The keys for Discord posts, newest first.</summary>
    private const string DiscordKeys = "1234567890";

    /// <summary>Letters the screen already uses for something else.</summary>
    private const string TakenLetters = "QT";

    private IReadOnlyList<Entry>? entries;
    private IReadOnlyList<string> items = [];

    /// <summary>Gets or sets the content store.</summary>
    [Inject]
    public required ContentStore Content { get; set; }

    /// <summary>Gets or sets the server API.</summary>
    [Inject]
    public required IBbsApi Api { get; set; }

    /// <summary>
    /// Gets the key of the file bulletin at a position: A, B, C... skipping the letters the screen already uses.
    /// </summary>
    /// <param name="index">The 0-based position.</param>
    /// <returns>The key, or <see langword="null"/> past Z.</returns>
    public static string? FileKey(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var letters = Enumerable.Range('A', 26).Select(c => (char)c).Where(c => !TakenLetters.Contains(c, StringComparison.Ordinal)).ToList();
        return index < letters.Count ? letters[index].ToString() : null;
    }

    /// <summary>
    /// Builds the list: Discord posts first (as many as fit), the offline line if needed, then the file bulletins.
    /// </summary>
    /// <param name="board">The file bulletins.</param>
    /// <param name="feed">The Discord bulletins feed.</param>
    /// <param name="height">How many lines fit.</param>
    /// <returns>The entries (each with its key and address) and the list lines.</returns>
    public static (IReadOnlyList<Entry> Entries, IReadOnlyList<string> Lines) Build(BulletinBoard board, FeedDto feed, int height)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(feed);
        var offline = feed.Enabled && !feed.Online;
        var files = board.Bulletins.Select((b, i) => (Bulletin: b, Key: FileKey(i))).Where(f => f.Key is not null).ToList();
        var room = Math.Max(0, height - files.Count - (offline ? 1 : 0));
        var posts = feed.Posts.Take(Math.Min(room, DiscordKeys.Length)).ToList();

        var entries = new List<Entry>();
        var lines = new List<string>();
        for (var i = 0; i < posts.Count; i++)
        {
            var post = posts[i];
            var key = DiscordKeys[i].ToString();
            var number = N(post.Number);
            entries.Add(new Entry(key, $"/bulletins/d/{number}", $"Bulletin #D{number}: {PipeCodes.StripToPlain(post.Title)}"));
            lines.Add(Ui.ItemPipe(key, $"Bulletin #D{number}: |15{TextLayout.Fit(post.Title, ListTitleWidth)}") + $"  |08({Ui.Stamp(post.PostedAt)})|07");
        }

        if (offline)
        {
            lines.Add(OfflineLine);
        }

        foreach (var (bulletin, key) in files)
        {
            var number = N(bulletin.Number);
            entries.Add(new Entry(key!, $"/bulletins/{number}", $"Bulletin #{number}: {bulletin.Title}"));
            var updated = string.IsNullOrEmpty(bulletin.Updated) ? string.Empty : $"  |08(updated {bulletin.Updated})|07";
            lines.Add(Ui.ItemPipe(key!, $"Bulletin #{number}: |15{bulletin.Title}") + updated);
        }

        return (entries, lines);
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var board = await Content.GetBulletinsAsync();
        var feed = await Api.GetFeedAsync(Feeds.Bulletins);
        var (built, lines) = Build(board, feed.Value ?? FeedDto.None, BulletinsArt.List.Height);
        items = lines;
        entries = built;
    }

    private static string N(int number) => number.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// One bulletin in the list.
    /// </summary>
    /// <param name="Key">The key that opens it.</param>
    /// <param name="Href">Its address.</param>
    /// <param name="Label">Its name, as plain text for the screen-reader mirror.</param>
    public sealed record Entry(string Key, string Href, string Label);
}
