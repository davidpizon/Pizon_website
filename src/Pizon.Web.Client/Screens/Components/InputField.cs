namespace Pizon.Web.Client.Screens.Components;

/// <summary>
/// One line of typing at an input slot (<c>@?NAME@</c>), bound with <c>@bind-Value</c>. Enter moves to the
/// form's next input, or submits the form from its last one.
/// </summary>
public sealed class InputField : TextInput
{
    /// <inheritdoc />
    internal override Task EnterAsync() => NextAsync();
}
