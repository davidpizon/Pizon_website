using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pizon.Doors.Sdk;

/// <summary>
/// The door registry loaded from <c>content/doors.json</c>. The BBS menu, the door host and the server's
/// score validation all read it, so a door can be hidden or locked without a rebuild.
/// </summary>
public sealed class DoorManifest
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Gets the registered doors in menu order.</summary>
    public IReadOnlyList<DoorManifestEntry> Doors { get; init; } = [];

    /// <summary>Gets the doors that are switched on.</summary>
    public IEnumerable<DoorManifestEntry> EnabledDoors => Doors.Where(d => d.Enabled);

    /// <summary>
    /// Parses a manifest from JSON.
    /// </summary>
    /// <param name="json">The <c>doors.json</c> text.</param>
    /// <returns>The parsed manifest.</returns>
    /// <exception cref="InvalidDataException">Thrown when the JSON is invalid or a door is missing required fields.</exception>
    public static DoorManifest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        DoorManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<DoorManifest>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("doors.json is not valid JSON.", ex);
        }

        manifest ??= new DoorManifest();
        foreach (var door in manifest.Doors)
        {
            if (string.IsNullOrWhiteSpace(door.Id) || string.IsNullOrWhiteSpace(door.EntryType) || door.Assemblies.Count == 0)
            {
                throw new InvalidDataException($"Door '{door.Id}' needs an id, an entryType and at least one assembly.");
            }
        }

        var duplicate = manifest.Doors.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Door id '{duplicate.Key}' is listed more than once.");
        }

        return manifest;
    }

    /// <summary>
    /// Finds a door by id (case-insensitive).
    /// </summary>
    /// <param name="id">The door id.</param>
    /// <returns>The entry, or <see langword="null"/> when unknown.</returns>
    public DoorManifestEntry? Find(string id) =>
        Doors.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One door in <c>doors.json</c>.
/// </summary>
public sealed class DoorManifestEntry
{
    /// <summary>Gets the stable door id (also the score board key).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the title shown in the door menu.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the one-line menu description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the lazy-loaded assembly files, for example <c>Pizon.Doors.Oracle.wasm</c>.</summary>
    public IReadOnlyList<string> Assemblies { get; init; } = [];

    /// <summary>Gets the full type name of the <see cref="IDoor"/> implementation.</summary>
    public string EntryType { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the door appears in the menu.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets a value indicating whether guests are locked out.</summary>
    public bool RequiresAuth { get; init; }

    /// <summary>Gets a value indicating whether the door keeps a shared score board.</summary>
    public bool Scored { get; init; }

    /// <summary>Gets how submitted scores are combined.</summary>
    public ScoreMode ScoreMode { get; init; } = ScoreMode.Max;

    /// <summary>Gets the largest score (or daily count) the server accepts; a sanity cap against tampering.</summary>
    public long MaxScore { get; init; } = 1_000_000;

    /// <summary>Gets the board title shown above the scores.</summary>
    public string ScoreTitle { get; init; } = "HIGH SCORES";

    /// <summary>Gets the optional intro ANSImation path, relative to the door's asset folder.</summary>
    public string? Intro { get; init; }

    /// <summary>
    /// Gets the static asset base path for the door (the Razor class library <c>_content</c> folder).
    /// </summary>
    [JsonIgnore]
    public string AssetBase =>
        Assemblies.Count == 0 ? string.Empty : $"_content/{Path.GetFileNameWithoutExtension(Assemblies[0])}/";
}

/// <summary>
/// How a door's scores are combined on the server.
/// </summary>
public enum ScoreMode
{
    /// <summary>Each player's best single score counts.</summary>
    Max,

    /// <summary>Each submission adds one to the player's count for today; the board shows today only.</summary>
    DailyCount,
}
