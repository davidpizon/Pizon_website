using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Doors;
using Pizon.Web.Client.Layout;
using Pizon.Web.Client.Pages;
using Pizon.Web.Client.Services;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// A navigation manager with a chosen base address that records navigations.
/// </summary>
internal sealed class TestNavigation : NavigationManager
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestNavigation"/> class.
    /// </summary>
    /// <param name="baseUri">The site root.</param>
    public TestNavigation(string baseUri) => Initialize(baseUri, baseUri);

    /// <summary>Gets a value indicating whether the last navigation forced a page load.</summary>
    public bool ForcedLoad { get; private set; }

    /// <inheritdoc />
    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        Uri = ToAbsoluteUri(uri).ToString();
        ForcedLoad = options.ForceLoad;
    }
}

/// <summary>
/// Tests for the JavaScript terminal bridge, using bUnit's JS interop fake.
/// </summary>
public sealed class JsTerminalTests : IAsyncDisposable
{
    private readonly BunitContext context = new();
    private readonly BunitJSModuleInterop module;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsTerminalTests"/> class.
    /// </summary>
    public JsTerminalTests()
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        module = context.JSInterop.SetupModule(JsTerminal.ModulePath);
        module.Mode = JSRuntimeMode.Loose;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => context.DisposeAsync();

    /// <summary>Output, transitions, settings, audio and links are forwarded to the module.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ForwardsCalls()
    {
        module.Setup<double>("playHandshake").SetResult(2.5);
        module.Setup<string?>("fetchArt", "a.ans").SetResult("ART");
        module.Setup<string?>("showRipUrl", "w.rip").SetResult("1");
        module.Setup<bool>("playAnimationUrl", _ => true).SetResult(true);
        module.Setup<string?>("storageGet", "k").SetResult("v");
        module.Setup<string>("localDate").SetResult("2026-11-15");
        await using var t = await JsTerminal.CreateAsync(context.JSInterop.JSRuntime, default, default, default);

        await t.WriteAsync("hi");
        await t.ClearAsync();
        await t.TransitionAsync(Transition.ScrollOff, "x");
        await t.SetLineModeAsync(true);
        await t.ApplySettingsAsync(new TerminalSettings(2400, true));
        await t.StopAudioAsync();
        await t.BeepAsync();
        await t.OpenUrlAsync(new Uri("https://example.com"));
        await t.SetAsync("k", "v");
        await t.DelayAsync(TimeSpan.FromMilliseconds(1));

        Assert.Equal(TimeSpan.FromSeconds(2.5), await t.PlayHandshakeAsync());
        Assert.Equal("ART", await t.LoadArtAsync("a.ans"));
        Assert.Equal("1", await t.ShowRipAsync("w.rip"));
        Assert.True(await t.PlayAnimationAsync("x.json", AnimationOptions.FullScreen with { MaxDuration = TimeSpan.FromSeconds(1) }));
        Assert.True(await t.PlayAnimationAsync("x.json", new AnimationOptions(1, 2, 3, 4, true, null)));
        Assert.Equal("v", await t.GetAsync("k"));
        Assert.Equal(new DateOnly(2026, 11, 15), await t.GetLocalDateAsync());

        Assert.Single(module.Invocations["init"]);
        Assert.Equal(2, module.Invocations["write"].Count);
        Assert.Equal("scrolloff", module.Invocations["transition"][0].Arguments[0]);
        Assert.Equal(2400, module.Invocations["setBaud"][0].Arguments[0]);
        Assert.Equal("https://example.com/", module.Invocations["openUrl"][0].Arguments[0]);
        var options = (Dictionary<string, object?>)module.Invocations["playAnimationUrl"][1].Arguments[1]!;
        Assert.Equal(3, options["width"]);
        Assert.False(options.ContainsKey("maxMs"));
    }

    /// <summary>Keys queue up, time out, and can be discarded; skips end waits early.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task HandlesKeysAndSkips()
    {
        await using var t = await JsTerminal.CreateAsync(context.JSInterop.JSRuntime, default, default, default);
        t.OnKey("a");
        Assert.Equal("a", await t.ReadKeyAsync());
        t.OnKey("b");
        Assert.Equal("b", await t.ReadKeyAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(await t.ReadKeyAsync(TimeSpan.FromMilliseconds(10)));
        t.OnKey("c");
        t.DiscardPendingKeys();
        Assert.Null(await t.ReadKeyAsync(TimeSpan.FromMilliseconds(10)));

        t.OnSkip();
        Assert.False(await t.WaitOrSkipAsync(TimeSpan.FromMilliseconds(10)));
        var waiting = t.WaitOrSkipAsync(TimeSpan.FromMinutes(1));
        t.OnSkip();
        Assert.True(await waiting);
        Assert.Equal(4, module.Invocations["armSkip"].Count);
    }

    /// <summary>An unreadable date falls back to today; effect names are lower case.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task FallsBackOnBadDates()
    {
        await using var t = await JsTerminal.CreateAsync(context.JSInterop.JSRuntime, default, default, default);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), await t.GetLocalDateAsync());
        Assert.Equal("spray", JsTerminal.EffectName(Transition.Spray));
    }
}

/// <summary>
/// Tests for presence and navigation services.
/// </summary>
public sealed class ServiceAdapterTests
{
    /// <summary>Hub messages update the node list; a hub that can't be reached leaves the board at node 1.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PresenceTracksNodes()
    {
        await using var presence = new HubPresence(new TestNavigation("http://127.0.0.1:9/"));
        var changes = 0;
        presence.Changed += () => changes++;
        await presence.DisconnectAsync();
        presence.Assign(3);
        presence.Update([3, 1]);
        Assert.Equal(3, presence.NodeNumber);
        Assert.Equal([1, 3], presence.Nodes);
        Assert.Equal(2, presence.Online);
        Assert.Equal(2, changes);

        await presence.ConnectAsync();
        await presence.ConnectAsync();
        await presence.DisconnectAsync();
        Assert.Equal(1, presence.NodeNumber);
        Assert.Equal(1, presence.Online);
    }

    /// <summary>External navigation forces a page load.</summary>
    [Fact]
    public void NavigatesAway()
    {
        var nav = new TestNavigation("http://localhost/");
        var external = new ExternalNavigator(nav);
        external.NavigateAway("/auth/discord");
        Assert.Equal(new Uri("http://localhost/auth/discord"), external.CurrentUri);
        Assert.True(nav.ForcedLoad);
    }
}

/// <summary>
/// bUnit tests for the Razor components.
/// </summary>
public sealed class ComponentTests : IAsyncDisposable
{
    private readonly BunitContext context = new();
    private readonly BunitJSModuleInterop module;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentTests"/> class.
    /// </summary>
    public ComponentTests()
    {
        var h = new Harness();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        module = context.JSInterop.SetupModule(JsTerminal.ModulePath);
        module.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IBbsApi>(h.Api);
        context.Services.AddSingleton(h.Content);
        context.Services.AddSingleton<IPresence>(h.Presence);
        context.Services.AddSingleton<IExternalNavigator>(h.Navigator);
        context.Services.AddSingleton<IDoorLoader>(h.Loader);
        context.Services.AddSingleton<TimeProvider>(h.Time);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => context.DisposeAsync();

    /// <summary>The home page renders the terminal and starts the BBS at the Connect screen.</summary>
    [Fact]
    public void HomeStartsTheBbs()
    {
        var cut = context.Render<Home>();
        Assert.NotNull(cut.Find(".term[role=log]"));
        Assert.NotNull(cut.Find("canvas.rip"));
        cut.WaitForAssertion(() => Assert.NotEmpty(module.Invocations["write"]));
        Assert.Single(module.Invocations["init"]);
    }

    /// <summary>The router shows the home page inside the layout.</summary>
    [Fact]
    public void RoutesToHome()
    {
        var cut = context.Render<Routes>();
        Assert.Contains("PIZON BBS terminal", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("blazor-error-ui", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>The layout wraps its body; the not-found page stays in character.</summary>
    [Fact]
    public void RendersLayoutAndNotFound()
    {
        var layout = context.Render<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddContent(0, "BODY"))));
        Assert.Contains("BODY", layout.Markup, StringComparison.Ordinal);
        Assert.Contains("TRY DRIVE B:", context.Render<NotFound>().Markup.ToUpperInvariant(), StringComparison.Ordinal);
    }
}
