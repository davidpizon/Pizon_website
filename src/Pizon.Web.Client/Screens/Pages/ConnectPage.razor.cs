using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// The first screen (<c>/</c>): a ready terminal waiting for the caller to press Connect. [C] or Enter dials:
/// the handshake plays, then the login screen replaces this address, so Back doesn't redial. [S] turns the
/// modem sounds on or off.
/// </summary>
public sealed partial class ConnectPage
{
    /// <summary>The URL of the optional logo sting played after CONNECT.</summary>
    public const string StingUrl = "content/anim/sting.json";

    /// <summary>How long the handshake lasts without sound.</summary>
    public static readonly TimeSpan SilentDuration = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Plays the dial-up handshake. Moving the mouse, tapping or pressing a key skips the waiting.
    /// </summary>
    /// <param name="term">The terminal.</param>
    /// <param name="cancellationToken">Cancelled when the caller leaves.</param>
    /// <returns>A task that completes after CONNECT and the logo sting.</returns>
    public static async Task HandshakeAsync(ITerminal term, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(term);
        await term.ClearAsync(cancellationToken);
        var audio = await term.PlayHandshakeAsync();
        var total = audio > TimeSpan.Zero ? audio : SilentDuration;
        var steps = new (string Text, double Share)[]
        {
            ("|07ATZ\r\n|10OK\r\n", 0.10),
            ("|07ATDT 555-0199\r\n", 0.25),
            ("|08(move mouse or press any key to skip)\r\n", 0.0),
            ("|08RINGING...\r\n", 0.30),
            ("|08CARRIER DETECTED · NEGOTIATING V.32bis · LAPM · V.42bis\r\n", 0.35),
        };

        foreach (var (text, share) in steps)
        {
            await term.WriteAsync(PipeCodes.ToAnsi(text), cancellationToken);
            if (share > 0 && await term.WaitOrSkipAsync(total * share, cancellationToken))
            {
                await term.StopAudioAsync();
                break;
            }
        }

        await term.WriteAsync(PipeCodes.ToAnsi("\r\n|15CONNECT 14400/ARQ/V32/LAPM/V42BIS|07\r\n"), cancellationToken);
        await term.DelayAsync(TimeSpan.FromMilliseconds(400), cancellationToken);
        term.DiscardPendingKeys();
        await term.PlayAnimationAsync(StingUrl, AnimationOptions.FullScreen with { MaxDuration = TimeSpan.FromSeconds(3) }, cancellationToken);
    }

    private async Task DialAsync()
    {
        Session.ConnectedAt = Host.Context.Time.GetUtcNow();
        await HandshakeAsync(Host.Terminal, Host.PageToken);
        Host.Navigate("/login", replace: true);
    }

    private async Task ToggleSoundAsync()
    {
        Session.Settings = Session.Settings with { Sound = !Session.Settings.Sound };
        await Session.Settings.SaveAsync(Host.Context.Storage);
        await Host.Terminal.ApplySettingsAsync(Session.Settings);
    }
}
