using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Pizon.Web.Client.Screens.Forms;

/// <summary>
/// The SysOp's delete-a-message form (<c>/sysop/delete</c>).
/// </summary>
public sealed class DeleteMessageModel
{
    /// <summary>The message shown when the number isn't one.</summary>
    public const string NoNumber = "Type the message number (digits only), then Enter.";

    /// <summary>Gets or sets the message number.</summary>
    [Required(ErrorMessage = NoNumber)]
    [RegularExpression("^[0-9]{1,9}$", ErrorMessage = NoNumber)]
    public string Number { get; set; } = string.Empty;

    /// <summary>Gets the message number, once it is valid.</summary>
    public long? Id => long.TryParse(Number, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
}
