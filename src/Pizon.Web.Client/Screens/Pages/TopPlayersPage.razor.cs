using System.Globalization;
using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// The Top Players bulletin (<c>/bulletins/top</c>): today's score boards of the scored doors, by the
/// caller's own date. The art draws first and the boards fill in as they arrive.
/// </summary>
public sealed partial class TopPlayersPage
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
    /// Builds the score board lines.
    /// </summary>
    /// <param name="boards">Each scored door with its board for the day.</param>
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> Lines(IEnumerable<(DoorManifestEntry Door, ApiResult<IReadOnlyList<ScoreEntry>> Scores)> boards)
    {
        ArgumentNullException.ThrowIfNull(boards);
        var result = new List<string>();
        foreach (var (door, scores) in boards)
        {
            result.Add($"|13{door.Title} - {door.ScoreTitle}|07");
            var entries = scores.Value ?? [];
            if (entries.Count == 0)
            {
                result.Add("|08  Nobody yet. Be the first.|07");
            }

            result.AddRange(entries.Select(e => string.Create(
                CultureInfo.InvariantCulture,
                $"  |14{e.Rank,2}.|07 {PipeCodes.Escape(TextWrap.Fit(e.Handle, 16))} |11{e.Score,8:N0}|07")));
            result.Add(string.Empty);
        }

        if (result.Count == 0)
        {
            result.Add("|08No scored doors are open right now.|07");
        }

        return result;
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var manifest = await Content.GetDoorsAsync();
        await Host.Ready;
        var day = (await Host.Terminal.GetLocalDateAsync()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var boards = new List<(DoorManifestEntry, ApiResult<IReadOnlyList<ScoreEntry>>)>();
        foreach (var door in manifest.EnabledDoors.Where(d => d.Scored))
        {
            boards.Add((door, await Api.GetScoresAsync(door.Id, day)));
        }

        lines = Lines(boards);
    }

    private void OnKey(string key)
    {
        if (lines is not null)
        {
            Host.Navigate("/bulletins");
        }
    }
}
