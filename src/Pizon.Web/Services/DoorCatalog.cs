using Microsoft.Extensions.FileProviders;
using Pizon.Doors.Sdk;

namespace Pizon.Web.Services;

/// <summary>
/// The door manifest as the server sees it, used to validate score posts.
/// </summary>
public interface IDoorCatalog
{
    /// <summary>
    /// Finds an enabled, scored door.
    /// </summary>
    /// <param name="id">The door id.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    DoorManifestEntry? FindScored(string id);
}

/// <summary>
/// Reads <c>content/doors.json</c> from the web root (the client's static files) once.
/// </summary>
public sealed class FileDoorCatalog : IDoorCatalog
{
    /// <summary>The manifest path under the web root.</summary>
    public const string ManifestPath = "content/doors.json";

    private readonly Lazy<DoorManifest> manifest;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileDoorCatalog"/> class.
    /// </summary>
    /// <param name="environment">The web host environment.</param>
    public FileDoorCatalog(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        manifest = new Lazy<DoorManifest>(() => Load(environment.WebRootFileProvider));
    }

    /// <summary>
    /// Loads the manifest from a file provider; a missing or broken file means no doors.
    /// </summary>
    /// <param name="files">The file provider.</param>
    /// <returns>The manifest.</returns>
    public static DoorManifest Load(IFileProvider files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var file = files.GetFileInfo(ManifestPath);
        if (!file.Exists)
        {
            return new DoorManifest();
        }

        using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        try
        {
            return DoorManifest.Parse(reader.ReadToEnd());
        }
        catch (InvalidDataException)
        {
            return new DoorManifest();
        }
    }

    /// <inheritdoc />
    public DoorManifestEntry? FindScored(string id) =>
        manifest.Value.Find(id) is { Enabled: true, Scored: true } entry ? entry : null;
}
