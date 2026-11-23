using System.Globalization;
using Microsoft.AspNetCore.Components;
using Pizon.Web.Client.Screens.Rendering;

namespace Pizon.Web.Client.Screens.Components;

/// <summary>
/// Binds a value to a slot of the enclosing <see cref="BbsScreen"/>'s art. Strings are pipe-code text
/// (escape visitor text with <c>PipeCodes.Escape</c>); numbers and dates use the invariant culture. When the
/// value changes while the screen is showing, only this slot is sent to the terminal. It renders no HTML.
/// </summary>
public sealed class Field : ComponentBase, IDisposable
{
    private FieldSlot? bound;

    /// <summary>Gets or sets the enclosing screen.</summary>
    [CascadingParameter]
    public BbsScreen? Screen { get; set; }

    /// <summary>Gets or sets the slot (a static property of the generated art class).</summary>
    [Parameter]
    [EditorRequired]
    public required FieldSlot Slot { get; set; }

    /// <summary>Gets or sets the value.</summary>
    [Parameter]
    public object? Value { get; set; }

    /// <summary>
    /// Turns a bound value into pipe-code text.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text: strings as they are, formattable values in the invariant culture, null as empty.</returns>
    public static string Text(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (bound is not null)
        {
            Screen?.ClearField(bound);
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var screen = Screen ?? throw new InvalidOperationException("A Field must be inside a BbsScreen.");
        if (bound is not null && bound != Slot)
        {
            screen.ClearField(bound);
        }

        bound = Slot;
        screen.SetField(Slot, Text(Value));
    }

    /// <inheritdoc />
    protected override bool ShouldRender() => false;
}
