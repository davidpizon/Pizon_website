namespace Pizon.Web.Client.Screens.Components;

/// <summary>
/// A block of typing at a multi-line input slot, bound with <c>@bind-Value</c>. Words wrap onto the next
/// line as you type and Enter starts a new line. A blank line (Enter twice), or Enter once the block is
/// full, finishes it: the form moves on or submits, as in a 1990s message editor.
/// </summary>
public sealed class BlockEditor : TextInput
{
    /// <inheritdoc />
    internal override async Task EnterAsync()
    {
        if (!Box.EndsWithBlankLine)
        {
            var before = Box.Text;
            var ansi = Box.NewLine();
            if (Box.Text != before)
            {
                await EditAsync(ansi);
                return;
            }
        }

        await EditAsync(Box.TrimEnd());
        await NextAsync();
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (!Box.Multiline)
        {
            throw new InvalidOperationException($"A BlockEditor needs a slot taller than one line; '{Slot.Name}' is one line (use InputField).");
        }
    }
}
