using System.Globalization;
using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// [5] User List and Hall of Fame (<c>/users</c>): the SysOp's hand-picked legends, recent signed-in callers
/// and the credits. The art draws first and the lists fill in as they arrive.
/// </summary>
public sealed partial class UsersPage
{
    private IReadOnlyList<string>? lines;
    private string? prompt;

    /// <summary>Gets or sets the content store.</summary>
    [Inject]
    public required ContentStore Content { get; set; }

    /// <summary>Gets or sets the server API.</summary>
    [Inject]
    public required IBbsApi Api { get; set; }

    /// <summary>
    /// Builds the hall of fame lines.
    /// </summary>
    /// <param name="fame">The hall of fame.</param>
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> RenderFame(HallOfFame fame)
    {
        ArgumentNullException.ThrowIfNull(fame);
        var result = new List<string> { "|13  HALL OF FAME|08 - legends of the board|07", string.Empty };
        foreach (var e in fame.Entries)
        {
            var handle = $"|15{PipeCodes.Escape(TextWrap.Fit(e.Handle, 16))}|07";
            if (e.Url is not null)
            {
                handle = "|{" + e.Url.AbsoluteUri + "}" + handle + "|]";
            }

            result.Add($"   {handle} |11{PipeCodes.Escape(TextWrap.Fit(e.Clearance, 9))}|07 {PipeCodes.Escape(TextWrap.Fit(e.Note, 48))}");
        }

        return result;
    }

    /// <summary>
    /// Builds the recent callers lines.
    /// </summary>
    /// <param name="callers">Recent callers.</param>
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> RenderCallers(IReadOnlyList<CallerDto> callers)
    {
        ArgumentNullException.ThrowIfNull(callers);
        var result = new List<string> { "|13  LAST CALLERS|08 - signed-in callers only; guests leave no trace|07", string.Empty };
        if (callers.Count == 0)
        {
            result.Add("|08   Nobody has signed the log yet.|07");
        }

        result.AddRange(callers.Select(c => string.Create(
            CultureInfo.InvariantCulture,
            $"   |15{PipeCodes.Escape(TextWrap.Fit(c.Handle, 16))}|07 |11LVL {c.Level,3}|07  |08last call {Ui.Stamp(c.LastSeen)}|07")));
        return result;
    }

    /// <summary>
    /// Builds the credits lines, including the attribution the CC BY-SA terminal font requires.
    /// </summary>
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> RenderCredits() =>
    [
        "|13  CREDITS|07",
        string.Empty,
        "   |07Font: IBM VGA 8x16 from |15|{https://int10h.org/oldschool-pc-fonts/}The Ultimate Oldschool PC Font Pack|]|07 by VileR,",
        "   |07licensed |{https://creativecommons.org/licenses/by-sa/4.0/}|11CC BY-SA 4.0|]|07.",
    ];

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var fame = await Content.GetHallOfFameAsync();
        var callers = await Api.GetRecentCallersAsync();
        var callerLines = RenderCallers(callers.Value ?? []).Concat(callers.IsOk ? [] : [Ui.FailureText(callers)]);
        lines = RenderFame(fame)
            .Concat([string.Empty]).Concat(callerLines)
            .Concat([string.Empty]).Concat(RenderCredits())
            .ToList();
    }

    private void OnKey(string key)
    {
        if (lines is not null)
        {
            Host.Navigate("/menu");
        }
    }
}
