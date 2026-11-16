namespace Pizon.Web.Client.Screens;

/// <summary>
/// One BBS screen: draws itself, handles keys and says where to go next.
/// </summary>
public interface IScreen
{
    /// <summary>Gets which screen this is.</summary>
    ScreenId Id { get; }

    /// <summary>
    /// Runs the screen until the caller leaves it.
    /// </summary>
    /// <param name="session">The current call.</param>
    /// <param name="cancellationToken">Cancelled when the page goes away.</param>
    /// <returns>The next screen.</returns>
    Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken);
}
