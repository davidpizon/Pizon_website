using System.Globalization;
using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Screens.Components;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// [4] Door Games (<c>/doors</c>): the door list from <c>doors.json</c>, with locks on doors that need
/// sign-in. A number runs the door in the terminal; the list draws again when it ends.
/// </summary>
public sealed partial class DoorsPage
{
    private List<DoorManifestEntry>? doors;
    private BbsScreen? screen;
    private (bool Member, IReadOnlyList<string> Lines)? items;

    /// <summary>Gets or sets the content store.</summary>
    [Inject]
    public required ContentStore Content { get; set; }

    /// <summary>
    /// Builds the door list: two lines per door (title and tags, then the description).
    /// </summary>
    /// <param name="doors">The enabled doors.</param>
    /// <param name="session">The call (for lock markers).</param>
    /// <returns>Pipe-code lines.</returns>
    public static IReadOnlyList<string> Lines(IReadOnlyList<DoorManifestEntry> doors, BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(session);
        var lines = new List<string>();
        if (doors.Count == 0)
        {
            lines.Add("|08All doors are closed for repairs. The SysOp is holding a screwdriver.|07");
        }

        for (var i = 0; i < doors.Count && i < 9; i++)
        {
            var d = doors[i];
            var locked = d.RequiresAuth && !session.IsMember;
            var tag = locked ? " |09[LOCKED - LVL 10]|07" : d.Scored ? " |13[SCORED]|07" : string.Empty;
            lines.Add(Ui.ItemPipe((i + 1).ToString(CultureInfo.InvariantCulture), $"|15{PipeCodes.Escape(d.Title)}") + tag);
            lines.Add($"|08    {PipeCodes.Escape(d.Description)}|07");
        }

        return lines;
    }

    /// <summary>Gets the list lines, built again only when the caller signs in or out.</summary>
    private IReadOnlyList<string> Items
    {
        get
        {
            if (items is not { } built || built.Member != Session.IsMember)
            {
                built = (Session.IsMember, Lines(doors!, Session));
                items = built;
            }

            return built.Lines;
        }
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync() => doors = (await Content.GetDoorsAsync()).EnabledDoors.ToList();

    private async Task EnterAsync(string key)
    {
        if (doors is null || !int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1 || n > Math.Min(9, doors.Count))
        {
            return;
        }

        await Host.Context.Doors.RunAsync(doors[n - 1], Session, Host.PageToken);
        screen?.Redraw();
    }
}
