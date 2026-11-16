using System.Text.Json;
using Pizon.Web.Client.Services;

namespace Pizon.Web.Client.Terminal;

/// <summary>
/// The caller's terminal preferences, remembered in the browser.
/// </summary>
/// <param name="Baud">0 (instant), 2400 or 9600.</param>
/// <param name="Sound">Whether modem sounds and beeps play (off by default).</param>
/// <param name="RipCard">Whether the RIPscrip welcome card is shown after login.</param>
public sealed record TerminalSettings(int Baud = 9600, bool Sound = false, bool RipCard = true)
{
    /// <summary>The storage key.</summary>
    public const string StorageKey = "pizon.settings";

    /// <summary>Gets the selectable baud rates, in the order the Settings screen cycles through them.</summary>
    public static IReadOnlyList<int> BaudRates { get; } = [0, 2400, 9600];

    /// <summary>Gets the baud rate shown to the caller.</summary>
    public string BaudLabel => Baud == 0 ? "INSTANT" : $"{Baud} BAUD";

    /// <summary>
    /// Gets the next baud rate in the cycle.
    /// </summary>
    /// <returns>Settings with the next rate.</returns>
    public TerminalSettings NextBaud()
    {
        var index = BaudRates.ToList().IndexOf(Baud);
        return this with { Baud = BaudRates[(index + 1) % BaudRates.Count] };
    }

    /// <summary>
    /// Loads settings, falling back to defaults when nothing (or nothing valid) is stored.
    /// </summary>
    /// <param name="storage">The browser storage.</param>
    /// <returns>The settings.</returns>
    public static async Task<TerminalSettings> LoadAsync(IBrowserStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var json = await storage.GetAsync(StorageKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new TerminalSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<TerminalSettings>(json) ?? new TerminalSettings();
            return BaudRates.Contains(settings.Baud) ? settings : settings with { Baud = 9600 };
        }
        catch (JsonException)
        {
            return new TerminalSettings();
        }
    }

    /// <summary>
    /// Saves the settings.
    /// </summary>
    /// <param name="storage">The browser storage.</param>
    /// <returns>A task that completes when saved.</returns>
    public Task SaveAsync(IBrowserStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        return storage.SetAsync(StorageKey, JsonSerializer.Serialize(this));
    }
}
