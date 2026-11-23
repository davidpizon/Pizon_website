using System.ComponentModel.DataAnnotations;

namespace Pizon.Web.Client.Screens.Forms;

/// <summary>
/// The handle form (<c>/handle</c> and <c>/handle/new</c>).
/// </summary>
public sealed class HandleModel
{
    /// <summary>The message shown for a handle that isn't 3-16 letters, digits, underscores or hyphens.</summary>
    public const string BadShape = "That handle won't fit through the phone line. 3-16 letters, digits, _ or -.";

    /// <summary>Gets or sets the handle (the server checks it again, and that it is free).</summary>
    [Required(ErrorMessage = BadShape)]
    [RegularExpression("^[A-Za-z0-9_-]{3,16}$", ErrorMessage = BadShape)]
    public string Handle { get; set; } = string.Empty;
}
