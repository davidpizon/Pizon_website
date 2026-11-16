using System.Diagnostics;
using Microsoft.AspNetCore.Components;

namespace Pizon.Web.Components.Pages;

/// <summary>
/// The server error page (production only), in character, with a request id for the logs.
/// </summary>
[ExcludeFromInteractiveRouting]
public sealed partial class ErrorPage
{
    /// <summary>Gets or sets the current request.</summary>
    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    /// <summary>Gets the request id to quote when reporting the error.</summary>
    public string? RequestId { get; private set; }

    /// <inheritdoc />
    protected override void OnInitialized() => RequestId = Activity.Current?.Id ?? HttpContext?.TraceIdentifier;
}
