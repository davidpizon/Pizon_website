using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens.Art;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// One bulletin from the Discord bulletins channel (<c>/bulletins/d/{n}</c>), a page at a time: who posted it
/// and when, then the message. The number is the post's fixed number, so the address never changes. An unknown
/// or deleted post shows "file not found".
/// </summary>
public sealed partial class DiscordBulletinPage
{
    private bool loaded;
    private FeedPostDto? post;
    private IReadOnlyList<string> lines = [];
    private string? prompt;

    /// <summary>Gets or sets the server API.</summary>
    [Inject]
    public required IBbsApi Api { get; set; }

    /// <summary>Gets or sets the post number from the address.</summary>
    [Parameter]
    public int Number { get; set; }

    /// <summary>
    /// Builds the lines of a post: a byline, a blank line, then the message wrapped to the width.
    /// </summary>
    /// <param name="post">The post.</param>
    /// <param name="width">The body width.</param>
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> Lines(FeedPostDto post, int width)
    {
        ArgumentNullException.ThrowIfNull(post);
        var edited = post.EditedAt is { } at ? $", edited {Ui.Stamp(at)}" : string.Empty;
        var result = new List<string> { $"|08Posted by |07{post.Author}|08 on {Ui.Stamp(post.PostedAt)}{edited}|07", string.Empty };
        result.AddRange(post.Lines.SelectMany(l => TextLayout.Wrap(l, width)));
        return result;
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var result = await Api.GetFeedPostAsync(Feeds.Bulletins, Number);
        post = result.Value;
        lines = post is null ? [] : Lines(post, BulletinArt.Body.Width);
        prompt = null;
        loaded = true;
    }

    private void LastPage() => prompt = Ui.AnyKey;
}
