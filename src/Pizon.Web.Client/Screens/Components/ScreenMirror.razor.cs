using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk.Text;

namespace Pizon.Web.Client.Screens.Components;

/// <summary>
/// The hidden, plain-HTML copy of a <see cref="BbsScreen"/> for screen readers: its bound text, its lists
/// and real links for its hotkeys. The terminal itself is a canvas of characters, so this is what assistive
/// technology reads.
/// </summary>
public sealed partial class ScreenMirror
{
    /// <summary>Gets or sets the screen to mirror.</summary>
    [Parameter]
    [EditorRequired]
    public required BbsScreen Screen { get; set; }

    /// <summary>
    /// Turns pipe-code text into plain text.
    /// </summary>
    /// <param name="pipeText">The text.</param>
    /// <returns>The visible characters, trimmed.</returns>
    public static string Plain(string pipeText) => PipeCodes.StripAnsi(PipeCodes.ToAnsi(pipeText)).Trim();
}
