using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Pizon.Web.Client.Screens.Forms;

/// <summary>
/// A message for the board (<c>/board/new</c>), or a reply (<c>/board/reply</c>, which also asks which
/// message it answers). A blank body abandons the message rather than failing validation.
/// </summary>
public sealed class ComposeModel : IValidatableObject
{
    /// <summary>The message shown when a reply doesn't say which message it answers.</summary>
    public const string NoNumber = "Which message? Type its number, then Enter.";

    /// <summary>Gets or sets a value indicating whether this is a reply (the number is required).</summary>
    public bool Reply { get; set; }

    /// <summary>Gets or sets the number of the message being answered (replies only).</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Gets or sets the message.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Gets the message number being answered, once it is a valid number.</summary>
    public long? ParentId => long.TryParse(Number, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Reply && ParentId is null)
        {
            yield return new ValidationResult(NoNumber, [nameof(Number)]);
        }
    }
}
