using Pizon.Doors.Sdk;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// [S] Terminal settings (<c>/settings</c>): baud rate, modem sounds and the RIP welcome card. Each key
/// changes one setting straight away, saves it in this browser and redraws just that line.
/// </summary>
public sealed partial class SettingsPage
{
    /// <summary>
    /// Applies one settings key.
    /// </summary>
    /// <param name="settings">The current settings.</param>
    /// <param name="key">The key pressed.</param>
    /// <returns>The new settings, or <see langword="null"/> when the key changes nothing.</returns>
    public static TerminalSettings? Apply(TerminalSettings settings, string key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (DoorKeys.Is(key, 'B'))
        {
            return settings.NextBaud();
        }

        if (DoorKeys.Is(key, 'A'))
        {
            return settings with { Sound = !settings.Sound };
        }

        return DoorKeys.Is(key, 'R') ? settings with { RipCard = !settings.RipCard } : null;
    }

    /// <summary>
    /// Shows a setting that is on or off.
    /// </summary>
    /// <param name="on">Whether it is on.</param>
    /// <returns>Pipe-code text.</returns>
    public static string OnOff(bool on) => on ? "|10ON" : "|09OFF";

    private async Task ChangeAsync(string key)
    {
        if (Apply(Session.Settings, key) is { } next)
        {
            Session.Settings = next;
            await next.SaveAsync(Host.Context.Storage);
            await Host.Terminal.ApplySettingsAsync(next);
        }
    }
}
