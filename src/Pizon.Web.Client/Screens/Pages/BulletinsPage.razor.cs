using System.Globalization;
using Microsoft.AspNetCore.Components;
using Pizon.Web.Client.Content;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// [1] System Bulletins (<c>/bulletins</c>): the SysOp's announcements, each a link to
/// <c>/bulletins/{n}</c>, plus the Top Players bulletin at <c>/bulletins/top</c>.
/// </summary>
public sealed partial class BulletinsPage
{
    private BulletinBoard? board;
    private IReadOnlyList<string> items = [];

    /// <summary>Gets or sets the content store.</summary>
    [Inject]
    public required ContentStore Content { get; set; }

    /// <summary>
    /// Builds the list lines.
    /// </summary>
    /// <param name="board">The bulletins.</param>
    /// <returns>One pipe-code line per bulletin.</returns>
    public static IReadOnlyList<string> Lines(BulletinBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return board.Bulletins.Select(b =>
        {
            var n = Number(b);
            var updated = string.IsNullOrEmpty(b.Updated) ? string.Empty : $"  |08(updated {b.Updated})|07";
            return Ui.ItemPipe(n, $"Bulletin #{n}: |15{b.Title}") + updated;
        }).ToList();
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var loaded = await Content.GetBulletinsAsync();
        items = Lines(loaded);
        board = loaded;
    }

    private static string Number(Bulletin bulletin) => bulletin.Number.ToString(CultureInfo.InvariantCulture);
}
