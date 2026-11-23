namespace Pizon.Web.Client.Screens.Hosting;

/// <summary>
/// Says who may open a screen page from its address. It must agree with the page's
/// <see cref="RouteTable"/> entry, which is what the BBS checks before the page draws (a test keeps the two
/// in step). The server stays the authority: this only decides what the caller is shown.
/// </summary>
/// <param name="access">Who may open the page.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BbsAccessAttribute(ScreenAccess access) : Attribute
{
    /// <summary>Gets who may open the page.</summary>
    public ScreenAccess Access { get; } = access;
}
