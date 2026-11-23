using System.Globalization;
using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens.Art;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// [2] Message Boards (<c>/board</c>): a threaded, text-only guestbook, a page at a time. Anyone can read;
/// [N] and [R] go to <c>/board/new</c> and <c>/board/reply</c>, where signed-in callers write.
/// </summary>
public sealed partial class BoardPage
{
    private IReadOnlyList<string>? lines;

    /// <summary>Gets or sets the server API.</summary>
    [Inject]
    public required IBbsApi Api { get; set; }

    /// <summary>
    /// Orders messages as threads: each top-level message followed by its replies (depth-first, oldest first).
    /// </summary>
    /// <param name="messages">Approved messages.</param>
    /// <returns>(message, depth) pairs in display order.</returns>
    public static IReadOnlyList<(MessageDto Message, int Depth)> Thread(IReadOnlyList<MessageDto> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var ids = messages.Select(m => m.Id).ToHashSet();
        var children = messages
            .Where(m => m.ParentId is { } p && ids.Contains(p))
            .GroupBy(m => m.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.PostedAt).ToList());
        var result = new List<(MessageDto, int)>();
        void Visit(MessageDto m, int depth)
        {
            result.Add((m, depth));
            if (children.TryGetValue(m.Id, out var kids))
            {
                foreach (var k in kids)
                {
                    Visit(k, Math.Min(depth + 1, 4));
                }
            }
        }

        foreach (var root in messages.Where(m => m.ParentId is null || !ids.Contains(m.ParentId.Value)).OrderBy(m => m.PostedAt))
        {
            Visit(root, 0);
        }

        return result;
    }

    /// <summary>
    /// Formats one message for the listing.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="depth">Its reply depth.</param>
    /// <returns>Pipe-code lines that fit the board's message block.</returns>
    public static IReadOnlyList<string> FormatMessage(MessageDto message, int depth)
    {
        ArgumentNullException.ThrowIfNull(message);
        var indent = new string(' ', 2 + (depth * 3));
        var marker = depth == 0 ? "■" : "└";
        var result = new List<string>
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"{indent}|#039{marker} |14#{message.Id}|08 · |15{PipeCodes.Escape(message.Handle)}|08 · {Ui.Stamp(message.PostedAt)}|07"),
        };
        var width = Math.Max(20, BoardArt.Body.Width - indent.Length - 2);
        result.AddRange(TextLayout.Wrap(PipeCodes.Escape(message.Body), width).Select(l => $"{indent}  |07{l}"));
        return result;
    }

    /// <summary>
    /// Builds the board lines for a result from the server.
    /// </summary>
    /// <param name="result">The approved messages.</param>
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> Lines(ApiResult<IReadOnlyList<MessageDto>> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var messages = result.Value ?? [];
        return !result.IsOk
            ? [Ui.FailureText(result)]
            : messages.Count == 0
                ? ["|08  The wall is blank. Somebody has to go first.|07"]
                : Thread(messages).SelectMany(t => FormatMessage(t.Message, t.Depth).Append(string.Empty)).ToList();
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync() => lines = Lines(await Api.GetMessagesAsync());
}
