using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Pizon.Web.Client.Screens;

namespace Pizon.Web.Client.Pages;

/// <summary>
/// The page for the addresses of screens that are still legacy <see cref="IScreen"/> classes (one
/// <c>@page</c> per such <see cref="RouteTable"/> entry; the others are pages in <c>Screens/Pages</c>). It
/// draws nothing itself: <see cref="Layout.BbsLayout"/> hosts the terminal and the BBS follows the address.
/// It keeps the browser tab title in step with the screen.
/// </summary>
public sealed partial class Home : IDisposable
{
    /// <summary>Gets or sets the navigation manager.</summary>
    [Inject]
    public required NavigationManager Navigation { get; set; }

    /// <summary>Gets the tab title for the current address.</summary>
    public string Title => TitleFor(new Uri(Navigation.Uri).AbsolutePath);

    /// <summary>
    /// Builds the tab title for a path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>"Screen - PIZON BBS", or just the site name.</returns>
    public static string TitleFor(string path) =>
        RouteTable.TryResolve(path, out var route) && route.Screen != ScreenId.Connect
            ? RouteTable.Tab(route.Title)
            : RouteTable.SiteName;

    /// <inheritdoc />
    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;

    /// <inheritdoc />
    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => _ = InvokeAsync(StateHasChanged);
}
