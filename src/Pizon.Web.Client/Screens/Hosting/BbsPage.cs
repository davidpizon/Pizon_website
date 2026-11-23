using Microsoft.AspNetCore.Components;

namespace Pizon.Web.Client.Screens.Hosting;

/// <summary>
/// Base class for screen pages (<c>@inherits BbsPage</c>): gives the page the host and the call, and
/// re-renders it before each draw so bound values show the call as it is now (a link opened before the
/// server said who is calling, say).
/// </summary>
public abstract class BbsPage : ComponentBase, IDisposable
{
    /// <summary>Gets or sets the page host.</summary>
    [Inject]
    public required BbsHost Host { get; set; }

    /// <summary>Gets the call.</summary>
    protected BbsSession Session => Host.Session;

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Re-renders the page.
    /// </summary>
    /// <returns>A task that completes when the render is queued.</returns>
    internal Task RefreshAsync() => InvokeAsync(() =>
    {
        OnBeforeDraw();
        StateHasChanged();
    });

    /// <inheritdoc />
    protected override void OnInitialized() => Host.AddRefresher(RefreshAsync);

    /// <summary>
    /// Runs just before the page's screen is drawn, once the call is known: the place to fill in defaults
    /// that depend on who is calling (a suggested handle, say).
    /// </summary>
    protected virtual void OnBeforeDraw()
    {
    }

    /// <summary>
    /// Renders the page now, in the middle of a long-running handler (so a status line changes while the
    /// handler goes on, as when paging the SysOp).
    /// </summary>
    /// <returns>A task that completes when the render has run.</returns>
    protected Task ShowAsync() => InvokeAsync(StateHasChanged);

    /// <summary>
    /// Stops refreshing the page.
    /// </summary>
    /// <param name="disposing">Whether this is a call to <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Host.RemoveRefresher(RefreshAsync);
        }
    }
}
