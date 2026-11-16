using System.Text.Json;
using Pizon.Doors.Sdk;

namespace Pizon.Web.Client.Content;

/// <summary>
/// Loads the editable content files under <c>wwwroot/content/</c> (cached for the session).
/// A missing or broken file yields empty content, never an exception.
/// </summary>
public sealed class ContentStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly HttpClient http;
    private readonly Dictionary<string, object> cache = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentStore"/> class.
    /// </summary>
    /// <param name="http">An HTTP client whose base address is the site root.</param>
    public ContentStore(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        this.http = http;
    }

    /// <summary>Loads the bulletins.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The bulletin board.</returns>
    public Task<BulletinBoard> GetBulletinsAsync(CancellationToken cancellationToken = default) =>
        LoadAsync<BulletinBoard>("content/bulletins.json", cancellationToken);

    /// <summary>Loads the file areas.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The file library.</returns>
    public Task<FileLibrary> GetFilesAsync(CancellationToken cancellationToken = default) =>
        LoadAsync<FileLibrary>("content/files.json", cancellationToken);

    /// <summary>Loads the Hall of Fame.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The Hall of Fame.</returns>
    public Task<HallOfFame> GetHallOfFameAsync(CancellationToken cancellationToken = default) =>
        LoadAsync<HallOfFame>("content/halloffame.json", cancellationToken);

    /// <summary>Loads the door manifest.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The manifest (empty when missing or invalid).</returns>
    public async Task<DoorManifest> GetDoorsAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue("doors", out var cached))
        {
            return (DoorManifest)cached;
        }

        DoorManifest manifest;
        try
        {
            manifest = DoorManifest.Parse(await http.GetStringAsync(new Uri("content/doors.json", UriKind.Relative), cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
        {
            manifest = new DoorManifest();
        }

        cache["doors"] = manifest;
        return manifest;
    }

    /// <summary>
    /// Loads a text file, returning <see langword="null"/> when it is missing.
    /// </summary>
    /// <param name="path">The path relative to the site root.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    public async Task<string?> GetTextAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<T> LoadAsync<T>(string path, CancellationToken cancellationToken)
        where T : new()
    {
        if (cache.TryGetValue(path, out var cached))
        {
            return (T)cached;
        }

        T value;
        try
        {
            value = await http.GetFromJsonAsync<T>(new Uri(path, UriKind.Relative), Options, cancellationToken) ?? new T();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            value = new T();
        }

        cache[path] = value;
        return value;
    }
}
