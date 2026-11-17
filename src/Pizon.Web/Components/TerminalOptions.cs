using System.Globalization;

namespace Pizon.Web.Components;

/// <summary>
/// Look-and-feel settings for the terminal, bound from the <c>Terminal</c> configuration section
/// (for example <c>Terminal__GlowBlur</c> in the server's <c>.env</c>).
/// </summary>
public sealed class TerminalOptions
{
    /// <summary>The glow blur used when the setting is missing or not a number.</summary>
    public const double DefaultGlowBlur = 0.1;

    /// <summary>The largest glow blur accepted; larger values are clamped to it.</summary>
    public const double MaxGlowBlur = 1.0;

    /// <summary>
    /// Gets or sets the phosphor glow blur radius, as a fraction of the terminal font size.
    /// 0 turns the glow off; values are clamped to 0..<see cref="MaxGlowBlur"/>.
    /// </summary>
    public double GlowBlur { get; set; } = DefaultGlowBlur;

    /// <summary>
    /// Gets the glow blur as a CSS number, clamped to the allowed range.
    /// </summary>
    public string GlowBlurCss
    {
        get
        {
            var blur = double.IsFinite(GlowBlur) ? Math.Clamp(GlowBlur, 0, MaxGlowBlur) : DefaultGlowBlur;
            return blur.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
