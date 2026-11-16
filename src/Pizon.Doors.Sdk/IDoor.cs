namespace Pizon.Doors.Sdk;

/// <summary>
/// A door game: an external program the BBS hands the terminal to, in the tradition of 1990s BBS doors.
/// Each door lives in its own lazy-loaded project and depends only on this SDK.
/// </summary>
public interface IDoor
{
    /// <summary>Gets the stable door id; it must match the <c>id</c> in <c>doors.json</c>.</summary>
    string Id { get; }

    /// <summary>Gets the title shown on the door's title card.</summary>
    string Title { get; }

    /// <summary>
    /// Runs the door until the caller leaves it. Returning (or throwing) hands the terminal back to the BBS.
    /// </summary>
    /// <param name="context">Everything the door may use: terminal I/O, caller, scores and storage.</param>
    /// <param name="cancellationToken">Signals that the caller disconnected.</param>
    /// <returns>A task that completes when the door exits.</returns>
    Task RunAsync(IDoorContext context, CancellationToken cancellationToken);
}
