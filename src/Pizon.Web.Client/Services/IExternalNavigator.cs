using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Pizon.Web.Client.Services;

/// <summary>
/// The browser address: in-app moves between screens (address bar and history), Back/Forward notifications,
/// and full-page navigation (the Discord sign-in redirect and reloading after logout).
/// </summary>
public interface IExternalNavigator
{
    /// <summary>Raised when the address changes, including Back and Forward. The argument is the new address.</summary>
    event EventHandler<Uri>? LocationChanged;

    /// <summary>Gets the current page URI.</summary>
    Uri CurrentUri { get; }

    /// <summary>Navigates the whole page, leaving the Blazor app.</summary>
    /// <param name="url">The URL, relative to the site root or absolute.</param>
    void NavigateAway(string url);

    /// <summary>Changes the address without leaving the app.</summary>
    /// <param name="path">The site-relative path.</param>
    /// <param name="replace">Replace the current history entry instead of adding one.</param>
    void Navigate(string path, bool replace);
}

/// <summary>
/// <see cref="IExternalNavigator"/> over <see cref="NavigationManager"/>.
/// </summary>
public sealed class ExternalNavigator : IExternalNavigator, IDisposable
{
    private readonly NavigationManager navigation;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalNavigator"/> class.
    /// </summary>
    /// <param name="navigation">The navigation manager.</param>
    public ExternalNavigator(NavigationManager navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        this.navigation = navigation;
        navigation.LocationChanged += OnLocationChanged;
    }

    /// <inheritdoc />
    public event EventHandler<Uri>? LocationChanged;

    /// <inheritdoc />
    public Uri CurrentUri => new(navigation.Uri);

    /// <inheritdoc />
    public void NavigateAway(string url) => navigation.NavigateTo(url, forceLoad: true);

    /// <inheritdoc />
    public void Navigate(string path, bool replace) => navigation.NavigateTo(path, new NavigationOptions { ReplaceHistoryEntry = replace });

    /// <inheritdoc />
    public void Dispose() => navigation.LocationChanged -= OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => LocationChanged?.Invoke(this, new Uri(e.Location));
}
