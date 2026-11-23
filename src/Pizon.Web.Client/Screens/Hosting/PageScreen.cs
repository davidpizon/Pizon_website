namespace Pizon.Web.Client.Screens.Hosting;

/// <summary>
/// Stands in the <see cref="BbsApp"/> loop for a screen that has become a page: the page itself is rendered by
/// the router at the screen's address, and this hands the terminal to <see cref="BbsHost"/>'s pump until the
/// address moves to another screen or the page leaves for one.
/// </summary>
/// <param name="context">The shared screen context.</param>
/// <param name="host">The page host.</param>
/// <param name="id">The screen.</param>
public sealed class PageScreen(ScreenContext context, BbsHost host, ScreenId id) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => id;

    /// <inheritdoc />
    public override Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken) =>
        host.RunPagesAsync(id, cancellationToken);
}
