using Microsoft.AspNetCore.Components;
using Pizon.Web.Client.Screens.Hosting;

namespace Pizon.Web.Client.Screens.Components;

/// <summary>
/// "File not found on this drive": what a page shows for an address under it that names nothing (an unknown
/// bulletin or file area). Any key goes back to the main menu.
/// </summary>
public sealed partial class FileNotFound
{
    /// <summary>Gets or sets the page host.</summary>
    [Inject]
    public required BbsHost Host { get; set; }
}
