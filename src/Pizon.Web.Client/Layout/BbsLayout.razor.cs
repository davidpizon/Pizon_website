using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Doors;
using Pizon.Web.Client.Screens;
using Pizon.Web.Client.Services;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Layout;

/// <summary>
/// The BBS layout: hosts the 80x25 terminal and the RIP canvas and runs the BBS in them. Every screen route
/// uses this layout, and Blazor keeps a layout alive while the address moves between pages that share it,
/// so the terminal and the call survive moving from screen to screen (and Back and Forward).
/// </summary>
public sealed partial class BbsLayout : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private ElementReference screen;
    private ElementReference mobileInput;
    private ElementReference ripCanvas;
    private JsTerminal? terminal;

    /// <summary>Gets or sets the JS runtime.</summary>
    [Inject]
    public required IJSRuntime Js { get; set; }

    /// <summary>Gets or sets the server API.</summary>
    [Inject]
    public required IBbsApi Api { get; set; }

    /// <summary>Gets or sets the content store.</summary>
    [Inject]
    public required ContentStore Content { get; set; }

    /// <summary>Gets or sets presence.</summary>
    [Inject]
    public required IPresence Presence { get; set; }

    /// <summary>Gets or sets the browser address and navigation.</summary>
    [Inject]
    public required IExternalNavigator Navigator { get; set; }

    /// <summary>Gets or sets the door loader.</summary>
    [Inject]
    public required IDoorLoader DoorLoader { get; set; }

    /// <summary>Gets or sets the clock.</summary>
    [Inject]
    public required TimeProvider Time { get; set; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        stop.Dispose();
        if (terminal is not null)
        {
            await terminal.DisposeAsync();
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        terminal = await JsTerminal.CreateAsync(Js, screen, mobileInput, ripCanvas);
        var session = new BbsSession { Settings = await TerminalSettings.LoadAsync(terminal) };
        await terminal.ApplySettingsAsync(session.Settings);
        var doors = new DoorHost(terminal, Api, Content, terminal, DoorLoader);
        var context = new ScreenContext(terminal, Api, Content, Presence, Navigator, terminal, doors, Time);
        var app = new BbsApp(context);
        var start = BbsApp.StartScreen(Navigator.CurrentUri, session);
        _ = app.RunAsync(session, start, stop.Token);
    }
}
