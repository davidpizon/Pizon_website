namespace Pizon.Doors.Sdk;

/// <summary>
/// The shared high-score board for a single door.
/// </summary>
public interface IDoorScores
{
    /// <summary>
    /// Submits a score for the current caller. Guests' scores are never saved.
    /// </summary>
    /// <param name="score">The score (or, for daily-count doors, the increment).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><see langword="true"/> when the server accepted the score.</returns>
    Task<bool> SubmitAsync(long score, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the top ten entries for this door.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The ranked entries, best first.</returns>
    Task<IReadOnlyList<ScoreEntry>> TopAsync(CancellationToken cancellationToken = default);
}
