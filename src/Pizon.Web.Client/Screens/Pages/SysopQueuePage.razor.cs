using System.Globalization;
using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// The moderation queue (<c>/sysop/queue</c>): one pending message at a time, to approve, reject, skip,
/// or reject and ban its author. The queue is fetched once the screen is on (never on a link alone), and
/// nothing changes until the SysOp presses a key.
/// </summary>
public sealed partial class SysopQueuePage
{
    private List<PendingMessageDto>? items;
    private string? failure;
    private string? status;
    private int index;

    /// <summary>Gets or sets the server API.</summary>
    [Inject]
    public required IBbsApi Api { get; set; }

    private PendingMessageDto? Current => items is not null && index < items.Count ? items[index] : null;

    private bool Finished => items is not null && Current is null;

    private string EmptyText => failure ?? (index > 0 ? "|10  That's the lot. The queue is clear.|07" : "|08  Nothing waiting. Go touch grass.|07");

    /// <summary>
    /// Builds the header line for one pending message.
    /// </summary>
    /// <param name="message">The pending message.</param>
    /// <param name="number">Its position (1-based).</param>
    /// <param name="total">How many are waiting.</param>
    /// <returns>Pipe-code text.</returns>
    public static string Head(PendingMessageDto message, int number, int total)
    {
        ArgumentNullException.ThrowIfNull(message);
        var reply = message.ParentId is { } p ? string.Create(CultureInfo.InvariantCulture, $" · reply to #{p}") : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"|08[{number}/{total}] |14#{message.Id}|08 from |15{PipeCodes.Escape(message.Handle)}|08 (user {message.UserId}) · {Ui.Stamp(message.PostedAt)}{reply}|07");
    }

    private async Task LoadAsync()
    {
        if (items is not null || !Session.IsSysop)
        {
            return;
        }

        var queue = await Api.GetModerationQueueAsync(Host.PageToken);
        failure = queue.IsOk ? null : Ui.FailureText(queue);
        items = queue.Value?.ToList() ?? [];
    }

    private Task ApproveAsync() => ActAsync(m => Api.ModerateAsync(m.Id, approve: true, Host.PageToken));

    private Task RejectAsync() => ActAsync(m => Api.ModerateAsync(m.Id, approve: false, Host.PageToken));

    private Task BanAsync() => ActAsync(async m =>
    {
        var banned = await Api.BanUserAsync(m.UserId, Host.PageToken);
        return banned.IsOk ? await Api.ModerateAsync(m.Id, approve: false, Host.PageToken) : banned;
    });

    private void Skip()
    {
        status = null;
        index++;
    }

    private async Task ActAsync(Func<PendingMessageDto, Task<ApiResult>> action)
    {
        if (Current is not { } message)
        {
            return;
        }

        var result = await action(message);
        if (result.IsOk)
        {
            Skip();
        }
        else
        {
            status = Ui.FailureText(result);
        }
    }

    private void OnKey(string key)
    {
        if (Finished)
        {
            Host.Navigate("/sysop");
        }
    }
}
