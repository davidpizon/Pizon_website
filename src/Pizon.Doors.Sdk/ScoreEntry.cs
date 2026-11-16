namespace Pizon.Doors.Sdk;

/// <summary>
/// One line on a high-score board.
/// </summary>
/// <param name="Rank">1-based rank.</param>
/// <param name="Handle">The player's handle.</param>
/// <param name="Score">The score.</param>
public sealed record ScoreEntry(int Rank, string Handle, long Score);
