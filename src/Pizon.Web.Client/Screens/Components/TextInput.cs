using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Pizon.Doors.Sdk;
using Pizon.Web.Client.Screens.Rendering;

namespace Pizon.Web.Client.Screens.Components;

/// <summary>
/// Base class for typing into an input slot (<c>@?NAME@</c>) of the enclosing <see cref="BbsScreen"/>:
/// <see cref="InputField"/> for one line, <see cref="BlockEditor"/> for a word-wrapped block. Bind it with
/// <c>@bind-Value</c>. While it has the cursor, every key goes to it before any hotkey; characters echo as
/// they are typed and Backspace erases. Inside a <see cref="BbsForm"/>, Enter moves to the form's next
/// input and submits from the last one, and Escape cancels the form. It renders no HTML.
/// </summary>
public abstract class TextInput : ComponentBase, IDisposable
{
    private FieldSlot? slot;

    /// <summary>Gets or sets the enclosing screen.</summary>
    [CascadingParameter]
    public BbsScreen? Screen { get; set; }

    /// <summary>Gets or sets the enclosing form, if any.</summary>
    [CascadingParameter]
    public BbsForm? Form { get; set; }

    /// <summary>Gets or sets the input slot (a <see cref="SlotKind.Input"/> slot of the art).</summary>
    [Parameter]
    [EditorRequired]
    public required FieldSlot Slot { get; set; }

    /// <summary>Gets or sets the text.</summary>
    [Parameter]
    public string Value { get; set; } = string.Empty;

    /// <summary>Gets or sets what happens when the caller changes the text (set by <c>@bind-Value</c>).</summary>
    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Gets or sets the bound model property (set by <c>@bind-Value</c>), which names the field for validation.</summary>
    [Parameter]
    public Expression<Func<string>>? ValueExpression { get; set; }

    /// <summary>Gets or sets the most characters accepted (by default whatever fits in the slot).</summary>
    [Parameter]
    public int? MaxLength { get; set; }

    /// <summary>Gets or sets a short name for screen readers (the mirror's label).</summary>
    [Parameter]
    public string? Label { get; set; }

    /// <summary>Gets the text being edited.</summary>
    internal TextBox Box { get; private set; } = null!;

    /// <summary>Gets the field this input edits, for validation messages.</summary>
    internal FieldIdentifier? FieldId => ValueExpression is null ? null : FieldIdentifier.Create(ValueExpression);

    /// <summary>Gets a value indicating whether the input takes keys now (its form isn't disabled).</summary>
    internal bool Enabled => Form is not { Disabled: true };

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Handles a key while the input has the cursor.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>A task that completes when the key is handled.</returns>
    internal async Task HandleKeyAsync(string key)
    {
        if (key == DoorKeys.Enter)
        {
            await EnterAsync();
        }
        else if (key == DoorKeys.Backspace)
        {
            await EditAsync(Box.Backspace());
        }
        else if (key == DoorKeys.Down)
        {
            Form?.Move(this, 1);
        }
        else if (key == DoorKeys.Up)
        {
            Form?.Move(this, -1);
        }
        else if (DoorKeys.IsPrintable(key))
        {
            await EditAsync(Box.Type(key));
        }
    }

    /// <summary>
    /// Handles Enter: finishes this input (moves on or submits) or, in a block, starts a new line.
    /// </summary>
    /// <returns>A task that completes when done.</returns>
    internal abstract Task EnterAsync();

    /// <summary>
    /// Finishes this input: moves to the form's next input, or submits the form from its last one.
    /// </summary>
    /// <returns>A task that completes when done.</returns>
    internal Task NextAsync() => Form is { } form ? form.NextAsync(this) : Task.CompletedTask;

    /// <summary>
    /// Sends an edit to the screen and, when the text changed, tells the page and the form about it. (Some
    /// edits change the text without changing the screen, such as dropping a trailing blank line.)
    /// </summary>
    /// <param name="ansi">The ANSI the edit produced (empty when the screen needs nothing).</param>
    /// <returns>A task that completes when the page has the new value.</returns>
    internal async Task EditAsync(string ansi)
    {
        if (Box.Text == Value)
        {
            if (ansi.Length > 0)
            {
                Screen!.WriteInput(this, ansi);
            }

            return;
        }

        Screen!.WriteInput(this, ansi);

        Value = Box.Text;
        Form?.FieldChanged(this);
        if (ValueChanged.HasDelegate)
        {
            await InvokeAsync(() => ValueChanged.InvokeAsync(Box.Text));
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var screen = Screen ?? throw new InvalidOperationException($"A {GetType().Name} must be inside a BbsScreen.");
        if (Slot.Kind != SlotKind.Input)
        {
            throw new InvalidOperationException($"'{Slot.Name}' is not an input slot (@?{Slot.Name}@).");
        }

        if (slot != Slot)
        {
            if (slot is not null)
            {
                screen.RemoveInput(this);
            }

            slot = Slot;
            Box = new TextBox(Slot, MaxLength ?? int.MaxValue);
            screen.AddInput(this);
            Form?.Add(this);
        }

        if (Value != Box.Text)
        {
            screen.WriteInput(this, Box.Set(Value ?? string.Empty));
        }
    }

    /// <inheritdoc />
    protected override bool ShouldRender() => false;

    /// <summary>
    /// Unregisters the input.
    /// </summary>
    /// <param name="disposing">Whether this is a call to <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && slot is not null)
        {
            Screen?.RemoveInput(this);
            Form?.Remove(this);
        }
    }
}
