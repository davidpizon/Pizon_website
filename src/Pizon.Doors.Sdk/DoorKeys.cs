namespace Pizon.Doors.Sdk;

/// <summary>
/// Names of the non-printable keys delivered by <see cref="IDoorContext.ReadKeyAsync"/>.
/// </summary>
public static class DoorKeys
{
    /// <summary>The Enter key.</summary>
    public const string Enter = "Enter";

    /// <summary>The Escape key.</summary>
    public const string Escape = "Escape";

    /// <summary>The Backspace key.</summary>
    public const string Backspace = "Backspace";

    /// <summary>The up arrow.</summary>
    public const string Up = "ArrowUp";

    /// <summary>The down arrow.</summary>
    public const string Down = "ArrowDown";

    /// <summary>The left arrow.</summary>
    public const string Left = "ArrowLeft";

    /// <summary>The right arrow.</summary>
    public const string Right = "ArrowRight";

    /// <summary>
    /// Checks whether a key is a single printable character.
    /// </summary>
    /// <param name="key">The key name.</param>
    /// <returns><see langword="true"/> for one printable character.</returns>
    public static bool IsPrintable(string? key) => key is { Length: 1 } && !char.IsControl(key[0]);

    /// <summary>
    /// Compares a key to a letter without regard to case.
    /// </summary>
    /// <param name="key">The key received.</param>
    /// <param name="letter">The letter to compare with.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    public static bool Is(string? key, char letter) =>
        key is { Length: 1 } && char.ToUpperInvariant(key[0]) == char.ToUpperInvariant(letter);
}
