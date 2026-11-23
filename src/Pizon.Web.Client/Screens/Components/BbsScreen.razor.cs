using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Screens.Hosting;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens.Components;

/// <summary>
/// One screen of a page: compiled art plus the values, lists, inputs and hotkeys bound to it by its child
/// <see cref="Field"/>, <see cref="List"/>, <see cref="InputField"/>, <see cref="BlockEditor"/> and
/// <see cref="Hotkey"/> components. While an input has the cursor, keys go to it before any hotkey. It writes nothing to the
/// HTML page except a hidden mirror for screen readers (<see cref="ScreenMirror"/>); <see cref="BbsHost"/>
/// draws it in the terminal as finished ANSI, and when a bound value changes while it is on screen it sends
/// just that slot, the way Razor re-renders only what changed.
/// </summary>
public sealed partial class BbsScreen : IDisposable
{
    private readonly Dictionary<FieldSlot, string> values = [];
    private readonly List<Hotkey> hotkeys = [];
    private readonly List<List> lists = [];
    private readonly List<TextInput> inputs = [];
    private readonly HashSet<object> changedWhileHidden = [];
    private readonly Queue<List> lastPages = new();
    private ScreenView? view;
    private string shownPrompt = string.Empty;
    private bool registered;

    /// <summary>Gets or sets the page host.</summary>
    [Inject]
    public required BbsHost Host { get; set; }

    /// <summary>Gets or sets the compiled art.</summary>
    [Parameter]
    [EditorRequired]
    public required ScreenArt Art { get; set; }

    /// <summary>Gets or sets the effect the screen is drawn with.</summary>
    [Parameter]
    public Transition Enter { get; set; } = Transition.Wipe;

    /// <summary>Gets or sets the effect played when a hotkey leaves the screen, if any.</summary>
    [Parameter]
    public Transition? Leave { get; set; }

    /// <summary>Gets or sets a value indicating whether a hotkey that leaves the screen is echoed at the cursor first.</summary>
    [Parameter]
    public bool EchoKeys { get; set; }

    /// <summary>Gets or sets pipe-code text shown at the <c>@CURSOR@</c> slot (a prompt), if any.</summary>
    [Parameter]
    public string? Prompt { get; set; }

    /// <summary>Gets or sets what happens to a key no hotkey or list took.</summary>
    [Parameter]
    public EventCallback<string> OnKey { get; set; }

    /// <summary>Gets or sets what happens once the screen has been drawn.</summary>
    [Parameter]
    public EventCallback OnDrawn { get; set; }

    /// <summary>Gets or sets the bound fields, lists and hotkeys.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>Gets the screen the page's address belongs to (set when the screen registers).</summary>
    public ScreenId? Screen { get; internal set; }

    /// <summary>Gets the draw version: it goes up when the page asks to be drawn again (<see cref="Redraw"/>).</summary>
    public int Version { get; private set; }

    /// <summary>Gets a value indicating whether this screen is on the terminal now.</summary>
    public bool IsLive => Host.Live == this;

    /// <summary>Gets the bound values in the art's slot order (for the mirror).</summary>
    internal IEnumerable<(FieldSlot Slot, string Value)> Values =>
        Art.Fields.Where(values.ContainsKey).Select(f => (f, values[f]));

    /// <summary>Gets the lists.</summary>
    internal IReadOnlyList<List> Lists => lists;

    /// <summary>Gets the inputs, in the order they were declared.</summary>
    internal IReadOnlyList<TextInput> Inputs => inputs;

    /// <summary>Gets the input that has the cursor, if any.</summary>
    internal TextInput? Active { get; private set; }

    /// <summary>Gets every slot something is bound to: fields, lists and their "more" slots.</summary>
    internal IEnumerable<FieldSlot> BoundSlots =>
        values.Keys.Concat(lists.Select(l => l.Slot)).Concat(lists.Where(l => l.More is not null).Select(l => l.More!))
            .Concat(inputs.Select(i => i.Slot));

    /// <summary>Gets the hotkeys, in the order they were declared.</summary>
    internal IReadOnlyList<Hotkey> Hotkeys => hotkeys;

    /// <summary>
    /// Asks for the whole screen to be drawn again (after a door or a download wrote over it).
    /// </summary>
    public void Redraw()
    {
        Version++;
        Host.Signal();
    }

    /// <summary>
    /// Changes the prompt straight away, in the middle of a key or callback (the page should set the value
    /// it passes as <see cref="Prompt"/> to the same text, so the next render keeps it).
    /// </summary>
    /// <param name="pipeText">The prompt, or <see langword="null"/> for none.</param>
    public void ShowPrompt(string? pipeText)
    {
        Prompt = pipeText;
        if (IsLive && view is not null)
        {
            PaintPrompt();
        }
    }

    /// <summary>
    /// Writes pipe-code text at the <c>@CURSOR@</c> slot, leaving the cursor there afterwards.
    /// </summary>
    /// <param name="pipeText">The text.</param>
    public void SayAtCursor(string pipeText)
    {
        ArgumentNullException.ThrowIfNull(pipeText);
        if (IsLive && view is not null)
        {
            Host.Write(view.CursorHome() + PipeCodes.ToAnsi(pipeText) + view.CursorHome());
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (registered)
        {
            Host.Remove(this);
        }
    }

    /// <summary>
    /// Composes the whole screen: the art, every bound value and list, and the prompt.
    /// </summary>
    /// <returns>Finished ANSI.</returns>
    internal string Compose()
    {
        changedWhileHidden.Clear();
        view = new ScreenView(Art);
        foreach (var (slot, value) in values.Where(v => Art.Fields.Contains(v.Key)))
        {
            view.Set(slot, value);
        }

        foreach (var list in lists.Where(Fits))
        {
            Bind(list);
        }

        foreach (var input in inputs.Where(i => Art.Fields.Contains(i.Slot)))
        {
            view.SetLines(input.Slot, input.Box.Lines.Select(PipeCodes.Escape));
        }

        shownPrompt = Prompt ?? string.Empty;
        return view.Render() + PromptAnsi();
    }

    /// <summary>
    /// Runs once the screen is on the terminal: sends whatever changed during the transition, then the
    /// page's <see cref="OnDrawn"/> and any list that is already on its last page.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the page is left.</param>
    /// <returns>A task that completes when done.</returns>
    internal async Task AfterDrawAsync(CancellationToken cancellationToken)
    {
        foreach (var change in changedWhileHidden.ToList())
        {
            switch (change)
            {
                case FieldSlot slot:
                    PaintField(slot);
                    break;
                case List list:
                    PaintList(list);
                    break;
                case TextInput input:
                    WriteInput(input, input.Box.Render());
                    break;
            }
        }

        changedWhileHidden.Clear();
        InputsChanged();
        if (shownPrompt != (Prompt ?? string.Empty))
        {
            PaintPrompt();
        }

        if (OnDrawn.HasDelegate)
        {
            await InvokeAsync(() => OnDrawn.InvokeAsync());
        }

        foreach (var list in lists)
        {
            QueueIfLastPage(list);
        }

        await RunQueuedAsync(cancellationToken);
    }

    /// <summary>
    /// Runs work queued while the screen was being rendered: a list that reached its last page.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the page is left.</param>
    /// <returns><see langword="true"/> when anything ran.</returns>
    internal async Task<bool> RunQueuedAsync(CancellationToken cancellationToken)
    {
        var ran = false;
        while (lastPages.TryDequeue(out var list))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ran = true;
            if (list.OnLastPage.HasDelegate)
            {
                await InvokeAsync(() => list.OnLastPage.InvokeAsync());
            }
        }

        return ran;
    }

    /// <summary>
    /// Handles a key: the first matching <see cref="Hotkey"/> wins; otherwise a list with more pages turns
    /// the page; otherwise the page's <see cref="OnKey"/> gets it.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancelled when the page is left.</param>
    /// <returns>A task that completes when the key is handled.</returns>
    internal async Task HandleKeyAsync(string key, CancellationToken cancellationToken)
    {
        if (Active is { } input)
        {
            if (key != DoorKeys.Escape)
            {
                await input.HandleKeyAsync(key);
                return;
            }

            if (input.Form is { Cancels: true } form)
            {
                await form.CancelAsync();
                return;
            }
        }

        if (hotkeys.FirstOrDefault(h => h.Matches(key)) is { } hotkey)
        {
            await PressAsync(hotkey, key, cancellationToken);
            return;
        }

        if (lists.FirstOrDefault(l => l.HasMore) is { } list)
        {
            list.Page++;
            PaintList(list);
            QueueIfLastPage(list);
            await RunQueuedAsync(cancellationToken);
            return;
        }

        if (OnKey.HasDelegate)
        {
            await InvokeAsync(() => OnKey.InvokeAsync(key));
        }
    }

    /// <summary>Binds a value to a slot (from a <see cref="Field"/>).</summary>
    /// <param name="slot">A slot of the art.</param>
    /// <param name="value">Pipe-code text.</param>
    internal void SetField(FieldSlot slot, string value)
    {
        if (values.TryGetValue(slot, out var old) && old == value)
        {
            return;
        }

        values[slot] = value;
        Changed(slot, () => PaintField(slot));
    }

    /// <summary>Unbinds a slot (a <see cref="Field"/> went away).</summary>
    /// <param name="slot">A slot of the art.</param>
    internal void ClearField(FieldSlot slot)
    {
        if (values.Remove(slot))
        {
            Changed(slot, () => PaintField(slot));
        }
    }

    /// <summary>Registers a list, or notes that its items changed.</summary>
    /// <param name="list">The list.</param>
    internal void SetList(List list)
    {
        if (!lists.Contains(list))
        {
            lists.Add(list);
        }

        Changed(list, () =>
        {
            PaintList(list);
            QueueIfLastPage(list);
            if (lastPages.Count > 0)
            {
                Host.Signal();
            }
        });
    }

    /// <summary>Removes a list.</summary>
    /// <param name="list">The list.</param>
    internal void RemoveList(List list) => lists.Remove(list);

    /// <summary>Registers an input.</summary>
    /// <param name="input">The input.</param>
    internal void AddInput(TextInput input)
    {
        inputs.Add(input);
        if (IsLive)
        {
            InputsChanged();
        }
    }

    /// <summary>Removes an input.</summary>
    /// <param name="input">The input.</param>
    internal void RemoveInput(TextInput input)
    {
        inputs.Remove(input);
        if (Active == input)
        {
            Active = null;
            InputsChanged();
        }
    }

    /// <summary>
    /// Sends an edit of an input to the terminal (when the screen is showing) and leaves the cursor where
    /// typing goes on.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="ansi">The edit, from its <see cref="TextBox"/>.</param>
    internal void WriteInput(TextInput input, string ansi)
    {
        if (view is not null && view.Art.Fields.Contains(input.Slot))
        {
            Changed(input, () => Host.Write(ansi + Home()));
        }
    }

    /// <summary>
    /// Gives an input the cursor (or takes it away from all of them).
    /// </summary>
    /// <param name="input">The input, or <see langword="null"/>.</param>
    internal void Focus(TextInput? input)
    {
        Active = input is { Enabled: true } && inputs.Contains(input) ? input : null;
        if (IsLive && view is not null)
        {
            Host.SetLineMode(Active is not null);
            Host.Write(Home());
        }
    }

    /// <summary>
    /// Checks which input should have the cursor after inputs came, went, or were switched on or off: the
    /// one that has it keeps it while it can take keys; otherwise the first input that can.
    /// </summary>
    internal void InputsChanged()
    {
        if (Active is { Enabled: true } active && inputs.Contains(active))
        {
            Focus(active);
            return;
        }

        Focus(inputs.FirstOrDefault(i => i.Enabled && (view is null || view.Art.Fields.Contains(i.Slot))));
    }

    /// <summary>Registers a hotkey.</summary>
    /// <param name="hotkey">The hotkey.</param>
    internal void AddHotkey(Hotkey hotkey) => hotkeys.Add(hotkey);

    /// <summary>Removes a hotkey.</summary>
    /// <param name="hotkey">The hotkey.</param>
    internal void RemoveHotkey(Hotkey hotkey) => hotkeys.Remove(hotkey);

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (view is not null && view.Art != Art)
        {
            Redraw();
        }
        else if (IsLive && shownPrompt != (Prompt ?? string.Empty))
        {
            PaintPrompt();
        }
    }

    /// <inheritdoc />
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            registered = true;
            Host.Add(this);
        }
    }

    private async Task PressAsync(Hotkey hotkey, string key, CancellationToken cancellationToken)
    {
        var leaves = hotkey.Href is not null || hotkey.Screen is not null;
        if (leaves && EchoKeys && key.Length == 1)
        {
            await Host.Terminal.WriteAsync(key.ToUpperInvariant(), cancellationToken);
        }

        if (leaves && Leave is { } effect)
        {
            await Host.Terminal.TransitionAsync(effect, Ui.Cls, cancellationToken);
        }

        if (hotkey.OnPress.HasDelegate)
        {
            await InvokeAsync(() => hotkey.OnPress.InvokeAsync(key));
        }

        if (hotkey.Screen is { } screen)
        {
            Host.Leave(screen);
        }
        else if (hotkey.Href is { } href)
        {
            Host.Navigate(href);
        }
    }

    private void Changed(object what, Action paint)
    {
        if (IsLive)
        {
            paint();
        }
        else if (view is not null)
        {
            changedWhileHidden.Add(what);
        }
    }

    private void Bind(List list)
    {
        view!.SetLines(list.Slot, list.Lines);
        if (list.More is { } more)
        {
            view.Set(more, list.HasMore ? list.MoreText : string.Empty);
        }
    }

    private void PaintField(FieldSlot slot)
    {
        if (!view!.Art.Fields.Contains(slot))
        {
            return;
        }

        if (values.TryGetValue(slot, out var value))
        {
            view!.Set(slot, value);
        }
        else
        {
            view!.Set(slot, string.Empty);
        }

        Host.Write(view.RenderField(slot) + PromptAnsi());
    }

    private void PaintList(List list)
    {
        if (!Fits(list))
        {
            return;
        }

        Bind(list);
        var ansi = view!.RenderField(list.Slot) + (list.More is { } more ? view.RenderField(more) : string.Empty);
        Host.Write(ansi + PromptAnsi());
    }

    private bool Fits(List list) => view!.Art.Fields.Contains(list.Slot) && (list.More is not { } more || view.Art.Fields.Contains(more));

    private void PaintPrompt()
    {
        var erase = view!.CursorHome() + new string(' ', PipeCodes.VisibleLength(shownPrompt));
        shownPrompt = Prompt ?? string.Empty;
        Host.Write(erase + PromptAnsi());
    }

    private string PromptAnsi() =>
        (shownPrompt.Length == 0 ? view!.CursorHome() : view!.CursorHome() + PipeCodes.ToAnsi(shownPrompt)) + (Active is null ? string.Empty : Home());

    private string Home() => Active is { } input && view!.Art.Fields.Contains(input.Slot) ? input.Box.Caret : view!.CursorHome();

    private void QueueIfLastPage(List list)
    {
        if (!list.HasMore && !list.LastPageSeen)
        {
            list.LastPageSeen = true;
            lastPages.Enqueue(list);
        }
    }
}
