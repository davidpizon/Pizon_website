using Microsoft.AspNetCore.Components;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens.Components;

/// <summary>
/// Plays compiled art in an animation slot (<c>@ANIM:NAME@</c>) of the enclosing <see cref="BbsScreen"/>,
/// behind the page: it starts once the screen is drawn and keeps playing while the caller reads, types or
/// presses hotkeys, since keys never go to it. Changing <see cref="Range"/> (or any other parameter) while
/// the screen is showing restarts it with the new frames. It renders no HTML.
/// </summary>
/// <example><c>&lt;Slot Art="SysopCamArt.Instance" At="BulletinCamArt.Cam" Range="@SysopCamArt.Ranges.Idle" /&gt;</c>.</example>
public sealed class Slot : ComponentBase, IDisposable
{
    private (ScreenArt Art, FieldSlot At, string? Range, bool Loop, TimeSpan? MaxDuration)? shown;

    /// <summary>Gets or sets the enclosing screen.</summary>
    [CascadingParameter]
    public BbsScreen? Screen { get; set; }

    /// <summary>Gets or sets the art to play (usually animated; still art just shows).</summary>
    [Parameter]
    [EditorRequired]
    public required ScreenArt Art { get; set; }

    /// <summary>Gets or sets the screen's animation slot the art plays in (it must fit).</summary>
    [Parameter]
    [EditorRequired]
    public required FieldSlot At { get; set; }

    /// <summary>Gets or sets the named range of frames to play (from the art's <c>Ranges</c>), or every frame.</summary>
    [Parameter]
    public string? Range { get; set; }

    /// <summary>Gets or sets a value indicating whether it starts again after its last frame.</summary>
    [Parameter]
    public bool Loop { get; set; } = true;

    /// <summary>Gets or sets how long it plays before resting on its first frame, if limited.</summary>
    [Parameter]
    public TimeSpan? MaxDuration { get; set; }

    /// <summary>Gets the frames and box to send to the terminal.</summary>
    internal SlotAnimation Animation
    {
        get
        {
            var range = Art.Range(Range);
            var frames = Enumerable.Range(range.First, range.Last - range.First + 1).ToList();
            return new SlotAnimation(
                At.Row,
                At.Column,
                Art.Columns,
                Art.Rows,
                frames.Select(Art.FrameAnsi).ToList(),
                frames.Select(i => Art.Frames[i].DelayMs).ToList(),
                Loop,
                MaxDuration);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (shown is not null)
        {
            Screen?.RemoveSlot(this);
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var screen = Screen ?? throw new InvalidOperationException("A Slot must be inside a BbsScreen.");
        if (At.Kind != SlotKind.Animation)
        {
            throw new InvalidOperationException($"'{At.Name}' is not an animation slot (@ANIM:{At.Name}@).");
        }

        if (Art.Columns > At.Width || Art.Rows > At.Height)
        {
            throw new InvalidOperationException($"The art '{Art.Name}' is {Art.Columns} x {Art.Rows}; the slot '{At.Name}' is {At.Width} x {At.Height}.");
        }

        _ = Art.Range(Range);
        var now = (Art, At, Range, Loop, MaxDuration);
        if (shown != now)
        {
            if (shown is { } before && before.At != At)
            {
                screen.RemoveSlot(this);
            }

            shown = now;
            screen.PlaySlot(this);
        }
    }

    /// <inheritdoc />
    protected override bool ShouldRender() => false;
}
