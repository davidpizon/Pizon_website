using Microsoft.AspNetCore.Components;

namespace Pizon.Web.Client.Services;

/// <summary>
/// Full-page navigation (needed for the Discord sign-in redirect and reloading after logout).
/// </summary>
public interface IExternalNavigator
{
    /// <summary>Gets the current page URI.</summary>
    Uri CurrentUri { get; }

    /// <summary>Navigates the whole page, leaving the Blazor app.</summary>
    /// <param name="url">The URL, relative to the site root or absolute.</param>
    void NavigateAway(string url);
}

/// <summary>
/// <see cref="IExternalNavigator"/> over <see cref="NavigationManager"/>.
/// </summary>
public sealed class ExternalNavigator : IExternalNavigator
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
    }

    /// <inheritdoc />
    public Uri CurrentUri => new(navigation.Uri);

    /// <inheritdoc />
    public void NavigateAway(string url) => navigation.NavigateTo(url, forceLoad: true);
}
