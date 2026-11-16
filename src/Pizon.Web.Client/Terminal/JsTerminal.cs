using System.Globalization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Pizon.Web.Client.Services;

namespace Pizon.Web.Client.Terminal;

/// <summary>
/// The browser terminal: forwards output to <c>wwwroot/js/terminal/index.js</c> and receives keys and
/// skip signals back through <see cref="JSInvokableAttribute"/> callbacks. Also provides localStorage.
/// </summary>
public sealed class JsTerminal : ITerminal, IBrowserStorage, IAsyncDisposable
{
    /// <summary>The path of the terminal module, relative to the site root.</summary>
    public const string ModulePath = "./js/terminal/index.js";

    private readonly IJSObjectReference module;
    private readonly Channel<string> keys = Channel.CreateUnbounded<string>();
    private DotNetObjectReference<JsTerminal>? self;
    private TaskCompletionSource<bool>? skip;

    private JsTerminal(IJSObjectReference module) => this.module = module;

    /// <summary>
    /// Loads the terminal module and attaches it to the page elements.
    /// </summary>
    /// <param name="js">The JS runtime.</param>
    /// <param name="screen">The terminal screen element.</param>
    /// <param name="mobileInput">The hidden input that brings up phone keyboards.</param>
    /// <param name="ripCanvas">The RIPscrip canvas.</param>
    /// <returns>The ready terminal.</returns>
    public static async Task<JsTerminal> CreateAsync(IJSRuntime js, ElementReference screen, ElementReference mobileInput, ElementReference ripCanvas)
    {
        ArgumentNullException.ThrowIfNull(js);
        var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        var terminal = new JsTerminal(module);
        terminal.self = DotNetObjectReference.Create(terminal);
        await module.InvokeVoidAsync("init", screen, terminal.self, mobileInput, ripCanvas);
        return terminal;
    }

    /// <summary>
    /// Receives a key from the page.
    /// </summary>
    /// <param name="key">The key name.</param>
    [JSInvokable]
    public void OnKey(string key) => keys.Writer.TryWrite(key);

    /// <summary>
    /// Receives a skip signal (mouse moved, screen tapped or key pressed while skip was armed).
    /// </summary>
    [JSInvokable]
    public void OnSkip() => skip?.TrySetResult(true);

    /// <inheritdoc />
    public Task WriteAsync(string ansi, CancellationToken cancellationToken = default) =>
        module.InvokeVoidAsync("write", cancellationToken, ansi).AsTask();

    /// <inheritdoc />
    public Task TransitionAsync(Transition effect, string ansi, CancellationToken cancellationToken = default) =>
        module.InvokeVoidAsync("transition", cancellationToken, EffectName(effect), ansi).AsTask();

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        WriteAsync("\u001b[0m\u001b[2J\u001b[H", cancellationToken);

    /// <inheritdoc />
    public async Task<string> ReadKeyAsync(CancellationToken cancellationToken = default) =>
        await keys.Reader.ReadAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<string?> ReadKeyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            return await keys.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void DiscardPendingKeys()
    {
        while (keys.Reader.TryRead(out _))
        {
        }
    }

    /// <inheritdoc />
    public async Task<bool> WaitOrSkipAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        skip = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await module.InvokeVoidAsync("armSkip", cancellationToken, true);
        try
        {
            var delay = Task.Delay(duration, cancellationToken);
            var winner = await Task.WhenAny(delay, skip.Task);
            await winner;
            return winner == skip.Task;
        }
        finally
        {
            skip = null;
            await module.InvokeVoidAsync("armSkip", CancellationToken.None, false);
        }
    }

    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) => Task.Delay(delay, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> PlayAnimationAsync(string url, AnimationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var js = new Dictionary<string, object?>
        {
            ["row"] = options.Row,
            ["col"] = options.Column,
            ["loop"] = options.Loop,
        };
        if (options.Width > 0)
        {
            js["width"] = options.Width;
            js["height"] = options.Height;
        }

        if (options.MaxDuration is { } max)
        {
            js["maxMs"] = max.TotalMilliseconds;
        }

        return await module.InvokeAsync<bool>("playAnimationUrl", cancellationToken, url, js);
    }

    /// <inheritdoc />
    public async Task<string?> LoadArtAsync(string url, CancellationToken cancellationToken = default) =>
        await module.InvokeAsync<string?>("fetchArt", cancellationToken, url);

    /// <inheritdoc />
    public async Task<string?> ShowRipAsync(string url, CancellationToken cancellationToken = default) =>
        await module.InvokeAsync<string?>("showRipUrl", cancellationToken, url);

    /// <inheritdoc />
    public Task SetLineModeAsync(bool enabled) => module.InvokeVoidAsync("setLineMode", enabled).AsTask();

    /// <inheritdoc />
    public async Task ApplySettingsAsync(TerminalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await module.InvokeVoidAsync("setBaud", settings.Baud);
        await module.InvokeVoidAsync("setSound", settings.Sound);
    }

    /// <inheritdoc />
    public async Task<TimeSpan> PlayHandshakeAsync() =>
        TimeSpan.FromSeconds(await module.InvokeAsync<double>("playHandshake"));

    /// <inheritdoc />
    public Task StopAudioAsync() => module.InvokeVoidAsync("stopAudio").AsTask();

    /// <inheritdoc />
    public Task BeepAsync() => module.InvokeVoidAsync("beep").AsTask();

    /// <inheritdoc />
    public Task OpenUrlAsync(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return module.InvokeVoidAsync("openUrl", url.AbsoluteUri).AsTask();
    }

    /// <inheritdoc />
    public async Task<DateOnly> GetLocalDateAsync()
    {
        var text = await module.InvokeAsync<string>("localDate");
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : DateOnly.FromDateTime(DateTime.Now);
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key) => await module.InvokeAsync<string?>("storageGet", key);

    /// <inheritdoc />
    public Task SetAsync(string key, string value) => module.InvokeVoidAsync("storageSet", key, value).AsTask();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        keys.Writer.TryComplete();
        self?.Dispose();
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is already gone; nothing to release.
        }
    }

    /// <summary>
    /// Gets the JavaScript name of an effect.
    /// </summary>
    /// <param name="effect">The effect.</param>
    /// <returns>The lower-case name used by <c>transitions.js</c>.</returns>
    public static string EffectName(Transition effect) => effect.ToString().ToLowerInvariant();
}
