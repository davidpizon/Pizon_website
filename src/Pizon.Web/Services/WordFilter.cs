using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Pizon.Web.Services;

/// <summary>
/// Options for the word filter (<c>WordFilter</c> section).
/// </summary>
public sealed class WordFilterOptions
{
    /// <summary>Gets or sets the blocked words (whole words, case-insensitive).</summary>
    public IList<string> Blocked { get; set; } = [];
}

/// <summary>
/// A simple whole-word blocklist for posts, pages, handles and scores. Approve-first moderation is the
/// real safeguard; this just saves the SysOp some reading.
/// </summary>
public sealed class WordFilter
{
    private static readonly string[] Defaults = ["viagra", "casino", "crypto-giveaway", "nigger", "faggot", "kike", "spic", "chink"];
    private readonly Regex pattern;

    /// <summary>
    /// Initializes a new instance of the <see cref="WordFilter"/> class.
    /// </summary>
    /// <param name="options">Extra blocked words from configuration.</param>
    public WordFilter(IOptions<WordFilterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var words = Defaults.Concat(options.Value.Blocked).Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => Regex.Escape(w.Trim())).Distinct().ToList();
        pattern = new Regex($@"(?<![\p{{L}}\p{{N}}])({string.Join('|', words)})(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// Checks text against the blocklist. Underscores and hyphens count as word breaks, so handles are
    /// checked per part.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when no blocked word appears.</returns>
    public bool IsClean(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return !pattern.IsMatch(text.Replace('_', ' '));
    }
}
