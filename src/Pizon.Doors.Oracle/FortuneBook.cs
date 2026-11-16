using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pizon.Doors.Oracle;

/// <summary>
/// The Oracle's fortunes, and the rule that picks one: the same question on the same day always gets
/// the same answer.
/// </summary>
public sealed class FortuneBook
{
    private static readonly string[] BuiltIn =
    [
        "The signs point to yes. The signs also point to the snack aisle.",
        "Reply hazy. Line noise detected. Ask again after the carrier settles.",
        "No. But the SysOp believes in you anyway.",
    ];

    private FortuneBook(IReadOnlyList<string> fortunes) => Fortunes = fortunes;

    /// <summary>Gets a small built-in book used when <c>oracle.json</c> is missing or empty.</summary>
    public static FortuneBook Default { get; } = new(BuiltIn);

    /// <summary>Gets the fortunes.</summary>
    public IReadOnlyList<string> Fortunes { get; }

    /// <summary>
    /// Parses <c>oracle.json</c> (<c>{ "fortunes": [ "…" ] }</c>), falling back to <see cref="Default"/>.
    /// </summary>
    /// <param name="json">The file text, or <see langword="null"/> when the file was not found.</param>
    /// <returns>The fortune book.</returns>
    public static FortuneBook Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("fortunes", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                var fortunes = list.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!.Trim())
                    .Where(s => s.Length > 0)
                    .ToArray();
                return fortunes.Length > 0 ? new FortuneBook(fortunes) : Default;
            }
        }
        catch (JsonException)
        {
            // A broken file must never break the door; the built-in fortunes take over.
        }

        return Default;
    }

    /// <summary>
    /// Picks the fortune for a question on a given day.
    /// </summary>
    /// <param name="question">The caller's question.</param>
    /// <param name="day">The caller's date.</param>
    /// <returns>The fortune text.</returns>
    public string Pick(string question, DateOnly day) => Fortunes[(int)(Seed(question, day) % (uint)Fortunes.Count)];

    /// <summary>
    /// Computes the stable seed for a question and day (FNV-1a over the normalized question and ISO date).
    /// </summary>
    /// <param name="question">The caller's question.</param>
    /// <param name="day">The caller's date.</param>
    /// <returns>A 32-bit seed that does not change between runs or machines.</returns>
    public static uint Seed(string question, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(question);
        var key = Normalize(question) + "|" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(key))
        {
            hash ^= b;
            hash *= 16777619u;
        }

        return hash;
    }

    /// <summary>
    /// Normalizes a question so trivial differences (case, spacing, trailing "?") ask the same thing.
    /// </summary>
    /// <param name="question">The raw question.</param>
    /// <returns>The normalized form.</returns>
    public static string Normalize(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var words = question.ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', words).TrimEnd('?', '!', '.', ' ');
    }
}
