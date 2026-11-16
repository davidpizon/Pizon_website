using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Components.WebAssembly.Services;
using Pizon.Doors.Sdk;

namespace Pizon.Web.Client.Doors;

/// <summary>
/// Loads a door's code and creates its entry type.
/// </summary>
public interface IDoorLoader
{
    /// <summary>
    /// Loads the door's assemblies (on first use) and creates the door.
    /// </summary>
    /// <param name="entry">The door's manifest entry.</param>
    /// <param name="cancellationToken">Cancels loading.</param>
    /// <returns>The door.</returns>
    /// <exception cref="InvalidOperationException">The entry type is missing or is not an <see cref="IDoor"/>.</exception>
    Task<IDoor> LoadAsync(DoorManifestEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads doors with Blazor's <see cref="LazyAssemblyLoader"/>, so a door's code downloads only when a caller
/// opens it.
/// </summary>
public sealed class LazyDoorLoader : IDoorLoader
{
    private readonly LazyAssemblyLoader loader;

    /// <summary>
    /// Initializes a new instance of the <see cref="LazyDoorLoader"/> class.
    /// </summary>
    /// <param name="loader">Blazor's lazy assembly loader.</param>
    public LazyDoorLoader(LazyAssemblyLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        this.loader = loader;
    }

    /// <inheritdoc />
    public async Task<IDoor> LoadAsync(DoorManifestEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var loaded = await loader.LoadAssembliesAsync(entry.Assemblies);
        var candidates = loaded.Concat(AppDomain.CurrentDomain.GetAssemblies());
        return Create(entry, candidates);
    }

    /// <summary>
    /// Finds the entry type in the given assemblies and creates it.
    /// </summary>
    /// <param name="entry">The door's manifest entry.</param>
    /// <param name="assemblies">Assemblies to search.</param>
    /// <returns>The door.</returns>
    /// <exception cref="InvalidOperationException">The entry type is missing or is not an <see cref="IDoor"/>.</exception>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Door assemblies are kept whole with TrimmerRootAssembly in the client project, so their entry types survive trimming.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Door assemblies are kept whole with TrimmerRootAssembly in the client project, so their constructors survive trimming.")]
    public static IDoor Create(DoorManifestEntry entry, IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(assemblies);
        var type = assemblies.Select(a => a.GetType(entry.EntryType, throwOnError: false)).FirstOrDefault(t => t is not null)
            ?? throw new InvalidOperationException($"Door type '{entry.EntryType}' was not found.");
        if (!typeof(IDoor).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"Door type '{entry.EntryType}' does not implement IDoor.");
        }

        return (IDoor)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Door type '{entry.EntryType}' could not be created."));
    }
}
