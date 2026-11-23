using System.Globalization;
using Pizon.Doors.Sdk.Text;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// [G] Goodbye (<c>/goodbye</c>): time online and a farewell, then the modem hangs up (NO CARRIER) and the
/// call goes back to the Connect screen, logged off.
/// </summary>
public sealed partial class GoodbyePage
{
    private TimeSpan online;

    /// <summary>
    /// Builds the farewell line.
    /// </summary>
    /// <param name="handle">The caller's handle.</param>
    /// <param name="online">How long the caller was online.</param>
    /// <returns>Pipe-code text.</returns>
    public static string Farewell(string handle, TimeSpan online)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var seconds = Math.Max(0, (long)online.TotalSeconds);
        return string.Create(CultureInfo.InvariantCulture, $"{PipeCodes.Escape(handle)}, you were online for {seconds:N0} seconds.");
    }

    /// <inheritdoc />
    protected override void OnBeforeDraw() => online = Host.Context.Time.GetUtcNow() - Session.ConnectedAt;

    private async Task HangUpAsync()
    {
        var term = Host.Terminal;
        await Host.Context.Presence.DisconnectAsync();
        await term.DelayAsync(TimeSpan.FromSeconds(2), Host.PageToken);
        await term.WriteAsync(PipeCodes.ToAnsi("|07+++\r\n"), Host.PageToken);
        await term.DelayAsync(TimeSpan.FromMilliseconds(600), Host.PageToken);
        await term.WriteAsync(PipeCodes.ToAnsi("|07ATH0\r\n\r\n|15NO CARRIER|07\r\n"), Host.PageToken);
        await term.DelayAsync(TimeSpan.FromSeconds(2), Host.PageToken);
        term.DiscardPendingKeys();
        Session.ActingAsGuest = false;
        Session.SignedOn = false;
        Session.ReturnTo = null;
        Host.Navigate("/", replace: true);
    }
}
