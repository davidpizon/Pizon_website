using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Screens.Rendering;

namespace Pizon.Web.Client.Screens.Components;

/// <summary>
/// A form on a screen: an <see cref="Microsoft.AspNetCore.Components.Forms.EditContext"/> over a model, the
/// way Blazor's <c>EditForm</c> works, for the <see cref="InputField"/> and <see cref="BlockEditor"/> inside
/// it. Add <c>&lt;DataAnnotationsValidator /&gt;</c> to validate with data annotations. Enter on the last
/// input submits: an invalid model shows its first message in the <see cref="StatusSlot"/> and puts the
/// cursor back in the field at fault; a valid one runs <see cref="OnValidSubmit"/>. Escape runs
/// <see cref="OnCancel"/>. The status slot shows the page's <see cref="Status"/> when there is no error.
/// </summary>
public sealed partial class BbsForm : IDisposable
{
    private readonly List<TextInput> inputs = [];
    private object? model;
    private string? error;
    private bool disabled;

    /// <summary>Gets or sets the enclosing screen.</summary>
    [CascadingParameter]
    public BbsScreen? Screen { get; set; }

    /// <summary>Gets or sets the model the inputs edit.</summary>
    [Parameter]
    [EditorRequired]
    public required object Model { get; set; }

    /// <summary>Gets or sets the slot that shows validation errors and, otherwise, <see cref="Status"/>.</summary>
    [Parameter]
    public FieldSlot? StatusSlot { get; set; }

    /// <summary>Gets or sets the page's own status text (pipe codes), shown when there is no validation error.</summary>
    [Parameter]
    public string? Status { get; set; }

    /// <summary>Gets or sets a value indicating whether the inputs stop taking keys (the form was sent).</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>Gets or sets what happens when the caller submits a valid model.</summary>
    [Parameter]
    public EventCallback<EditContext> OnValidSubmit { get; set; }

    /// <summary>Gets or sets what happens when the caller presses Escape in the form.</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>Gets or sets the inputs (and validator).</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>Gets the edit context over <see cref="Model"/>.</summary>
    public EditContext EditContext { get; private set; } = null!;

    /// <summary>Gets the validation error on show, as pipe-code text, if any.</summary>
    public string? Error => error;

    /// <summary>Gets the form's inputs, in the order they were declared.</summary>
    internal IReadOnlyList<TextInput> Inputs => inputs;

    /// <summary>Gets a value indicating whether Escape does something in this form.</summary>
    internal bool Cancels => OnCancel.HasDelegate;

    /// <summary>
    /// Validates the model and, when it is valid, runs <see cref="OnValidSubmit"/>.
    /// </summary>
    /// <returns>A task that completes when the submit has been handled.</returns>
    public async Task SubmitAsync()
    {
        if (Disabled)
        {
            return;
        }

        if (!await EditContext.ValidateAsync())
        {
            var messages = EditContext.GetValidationMessages().ToList();
            error = "|09" + PipeCodes.Escape(messages[0]) + "|07";
            ShowStatus();
            var wrong = inputs.FirstOrDefault(i => i.FieldId is { } id && EditContext.GetValidationMessages(id).Any());
            if (wrong is not null)
            {
                Screen!.Focus(wrong);
            }

            return;
        }

        error = null;
        ShowStatus();
        if (OnValidSubmit.HasDelegate)
        {
            await InvokeAsync(() => OnValidSubmit.InvokeAsync(EditContext));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (StatusSlot is not null)
        {
            Screen?.ClearField(StatusSlot);
        }
    }

    /// <summary>Adds an input (in declaration order).</summary>
    /// <param name="input">The input.</param>
    internal void Add(TextInput input) => inputs.Add(input);

    /// <summary>Removes an input.</summary>
    /// <param name="input">The input.</param>
    internal void Remove(TextInput input) => inputs.Remove(input);

    /// <summary>
    /// Finishes an input: the cursor moves to the next input, or the form is submitted from the last one.
    /// </summary>
    /// <param name="input">The input the caller finished.</param>
    /// <returns>A task that completes when done.</returns>
    internal Task NextAsync(TextInput input)
    {
        var index = inputs.IndexOf(input);
        if (index >= 0 && index < inputs.Count - 1)
        {
            Screen!.Focus(inputs[index + 1]);
            return Task.CompletedTask;
        }

        return SubmitAsync();
    }

    /// <summary>
    /// Moves the cursor to another input of the form (the arrow keys).
    /// </summary>
    /// <param name="input">The input that has the cursor.</param>
    /// <param name="step">+1 for the next input, -1 for the previous one.</param>
    internal void Move(TextInput input, int step)
    {
        var index = inputs.IndexOf(input) + step;
        if (index >= 0 && index < inputs.Count)
        {
            Screen!.Focus(inputs[index]);
        }
    }

    /// <summary>Runs <see cref="OnCancel"/>.</summary>
    /// <returns>A task that completes when done.</returns>
    internal Task CancelAsync() => InvokeAsync(() => OnCancel.InvokeAsync());

    /// <summary>
    /// Notes that the caller typed in an input: validation hears about it and an error on show is cleared.
    /// </summary>
    /// <param name="input">The input.</param>
    internal void FieldChanged(TextInput input)
    {
        if (input.FieldId is { } id)
        {
            EditContext.NotifyFieldChanged(id);
        }

        if (error is not null)
        {
            error = null;
            ShowStatus();
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var screen = Screen ?? throw new InvalidOperationException("A BbsForm must be inside a BbsScreen.");
        if (!ReferenceEquals(model, Model))
        {
            model = Model;
            EditContext = new EditContext(Model);
            error = null;
        }

        ShowStatus();
        if (disabled != Disabled)
        {
            disabled = Disabled;
            screen.InputsChanged();
        }
    }

    private void ShowStatus()
    {
        if (StatusSlot is not null)
        {
            Screen!.SetField(StatusSlot, error ?? Status ?? string.Empty);
        }
    }
}
