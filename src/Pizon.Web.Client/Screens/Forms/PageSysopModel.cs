namespace Pizon.Web.Client.Screens.Forms;

/// <summary>
/// The Page SysOp form (<c>/page</c>). A blank message cancels the page rather than failing validation.
/// </summary>
public sealed class PageSysopModel
{
    /// <summary>Gets or sets the message for the SysOp.</summary>
    public string Message { get; set; } = string.Empty;
}
