using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;

namespace Pizon.Doors.TemplateDoor;

/// <summary>
/// A starter door: greets the caller, counts key presses, and leaves on Q or ESC.
/// </summary>
public sealed class TemplateDoor : IDoor
{
    /// <inheritdoc />
    public string Id => "template-door-id";

    /// <inheritdoc />
    public string Title => "Template Door";

    /// <inheritdoc />
    public async Task RunAsync(IDoorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await context.ClearAsync(cancellationToken);
        await context.WriteAsync(
            PipeCodes.ToAnsi($"|14Hello, |15{PipeCodes.Escape(context.Caller.Handle)}|14!|07\r\n\r\nPress keys. |11Q|07 leaves.\r\n"),
            cancellationToken);
        var presses = 0;
        while (true)
        {
            var key = await context.ReadKeyAsync(cancellationToken);
            if (DoorKeys.Is(key, 'Q') || key == DoorKeys.Escape)
            {
                break;
            }

            presses++;
            await context.WriteAsync(PipeCodes.ToAnsi($"\r|07Presses: |11{presses}|07   "), cancellationToken);
        }

        if (context.Caller.IsAuthenticated && presses > 0)
        {
            await context.Scores.SubmitAsync(presses, cancellationToken);
        }
    }
}
