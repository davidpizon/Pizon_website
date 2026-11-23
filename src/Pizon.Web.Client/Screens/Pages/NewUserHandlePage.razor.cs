namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// The first sign-in (<c>/handle/new</c>): a caller who signed in with Discord but has no handle yet picks one, then carries on to where they were heading. Escape carries on as GUEST for this call.
/// </summary>
public sealed partial class NewUserHandlePage
{
    /// <inheritdoc />
    protected override bool ForNewUser => true;
}
