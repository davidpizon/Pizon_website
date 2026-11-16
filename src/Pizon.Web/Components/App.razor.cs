using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

namespace Pizon.Web.Components;

/// <summary>
/// The HTML shell. The whole app runs in WebAssembly without prerendering; a boot message covers the
/// download, and a noscript link points to <c>/plain</c>. Pages marked
/// <see cref="ExcludeFromInteractiveRoutingAttribute"/> (the error page) render on the server instead.
/// </summary>
public sealed partial class App
{
    /// <summary>Gets the interactive render mode: WebAssembly, no prerendering.</summary>
    public static IComponentRenderMode InteractiveMode { get; } = new InteractiveWebAssemblyRenderMode(prerender: false);

    /// <summary>Gets or sets the current request.</summary>
    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    /// <summary>Gets the render mode for this request: interactive, or static for server-only pages.</summary>
    public IComponentRenderMode? RenderMode => HttpContext?.AcceptsInteractiveRouting() == false ? null : InteractiveMode;
}
