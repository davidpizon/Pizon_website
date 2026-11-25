using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pizon.Doors.Sdk.Text;

namespace Pizon.Web.Services.Discord;

/// <summary>
/// Turns a Discord message into a bulletin: a title (the first line) and body lines, in pipe-code text.
/// Everything the message says is escaped first, since anyone in the channel can post; only the colors added
/// here are real pipe codes. Bold and headers get bright colors, mentions and custom emoji become names, links
/// stay as text, and attachments and embeds become lines of their own.
/// </summary>
public static partial class DiscordText
{
    /// <summary>The longest title, in visible characters.</summary>
    public const int TitleWidth = 60;

    /// <summary>The title used when a message has no words at all.</summary>
    public const string Untitled = "(no text)";

    /// <summary>
    /// Converts a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The title and the body lines. A one-line message is its own body.</returns>
    public static (string Title, IReadOnlyList<string> Lines) Convert(DiscordMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var names = message.Mentions.Where(m => m.Id.Length > 0).DistinctBy(m => m.Id).ToDictionary(m => m.Id, m => m.Name, StringComparer.Ordinal);
        var raw = message.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var first = raw.FindIndex(l => !string.IsNullOrWhiteSpace(l) && !IsFence(l));

        var lines = new List<string>();
        string title;
        if (first < 0)
        {
            title = Untitled;
        }
        else
        {
            title = TextLayout.Fit(Inline(LinePrefix().Replace(raw[first].Trim(), string.Empty), names, color: false), TitleWidth);
            var rest = raw.Skip(first + 1).SkipWhile(string.IsNullOrWhiteSpace).ToList();
            lines.AddRange(Body(rest.Count > 0 ? rest : [raw[first]], names));
        }

        foreach (var embed in message.Embeds)
        {
            lines.AddRange(Embed(embed, names));
        }

        foreach (var file in message.Attachments)
        {
            lines.Add($"|08[{(file.IsImage ? "image" : "file")}: {PipeCodes.Escape(file.Filename)}]|07");
        }

        if (title == Untitled && message.Attachments.Count > 0)
        {
            title = TextLayout.Fit(PipeCodes.Escape(message.Attachments[0].Filename), TitleWidth);
        }

        return (title, lines);
    }

    /// <summary>
    /// Converts one line of Discord markdown to pipe-code text, escaping everything that is not markup.
    /// </summary>
    /// <param name="raw">The raw text.</param>
    /// <param name="names">Display names of mentioned users, by id.</param>
    /// <param name="color">Whether bold text gets a bright color (titles and headers stay one color).</param>
    /// <returns>Pipe-code text.</returns>
    public static string Inline(string raw, IReadOnlyDictionary<string, string> names, bool color)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(names);
        var sb = new StringBuilder(raw.Length + 16);
        var at = 0;
        foreach (Match m in Token().Matches(raw))
        {
            sb.Append(PipeCodes.Escape(raw[at..m.Index]));
            sb.Append(Replace(m, names, color));
            at = m.Index + m.Length;
        }

        return sb.Append(PipeCodes.Escape(raw[at..])).ToString();
    }

    private static IEnumerable<string> Body(IEnumerable<string> raw, IReadOnlyDictionary<string, string> names)
    {
        foreach (var line in raw)
        {
            if (IsFence(line))
            {
                continue;
            }

            var header = Header().Match(line);
            if (header.Success)
            {
                yield return "|14" + Inline(header.Groups["text"].Value, names, color: false) + "|07";
                continue;
            }

            var small = Subtext().Match(line);
            if (small.Success)
            {
                yield return "|08" + Inline(small.Groups["text"].Value, names, color: false) + "|07";
                continue;
            }

            var quote = Quote().Match(line);
            yield return quote.Success
                ? "|08>|07 " + Inline(quote.Groups["text"].Value, names, color: true)
                : Inline(line, names, color: true);
        }
    }

    private static IEnumerable<string> Embed(DiscordEmbed embed, IReadOnlyDictionary<string, string> names)
    {
        if (!string.IsNullOrWhiteSpace(embed.Title))
        {
            yield return "|15" + Inline(embed.Title, names, color: false) + "|07";
        }

        if (!string.IsNullOrWhiteSpace(embed.Description))
        {
            foreach (var line in Body(embed.Description.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'), names))
            {
                yield return line;
            }
        }

        if (!string.IsNullOrWhiteSpace(embed.Url))
        {
            yield return "|08" + PipeCodes.Escape(embed.Url) + "|07";
        }
    }

    private static string Replace(Match m, IReadOnlyDictionary<string, string> names, bool color)
    {
        string Group(string name) => m.Groups[name].Value;

        if (m.Groups["emoji"].Success)
        {
            return PipeCodes.Escape($":{Group("emoji")}:");
        }

        if (m.Groups["user"].Success)
        {
            return "@" + PipeCodes.Escape(names.TryGetValue(Group("user"), out var name) ? name : "user");
        }

        if (m.Groups["role"].Success)
        {
            return "@role";
        }

        if (m.Groups["channel"].Success)
        {
            return "#channel";
        }

        if (m.Groups["time"].Success)
        {
            return long.TryParse(Group("time"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds) &&
                seconds is >= -62_135_596_800 and <= 253_402_300_799
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
                : PipeCodes.Escape(m.Value);
        }

        if (m.Groups["url"].Success)
        {
            return PipeCodes.Escape(Group("url"));
        }

        if (m.Groups["linktext"].Success)
        {
            return Inline(Group("linktext"), names, color) + " (" + PipeCodes.Escape(Group("linkurl")) + ")";
        }

        if (m.Groups["bold"].Success)
        {
            var inner = Inline(Group("bold"), names, color);
            return color ? "|15" + inner + "|07" : inner;
        }

        if (m.Groups["code"].Success)
        {
            return PipeCodes.Escape(Group("code"));
        }

        // Underline, strike-through and spoilers: the words stay, the markers go.
        return Inline(Group("plain"), names, color);
    }

    private static bool IsFence(string line) => line.TrimStart().StartsWith("```", StringComparison.Ordinal);

    [GeneratedRegex(@"<a?:(?<emoji>\w+):\d+>|<@!?(?<user>\d+)>|<@&(?<role>\d+)>|<#(?<channel>\d+)>|<t:(?<time>-?\d+)(?::[tTdDfFR])?>|<(?<url>https?://[^>\s]+)>|\[(?<linktext>[^\]]+)\]\((?<linkurl>https?://[^)\s]+)\)|\*\*(?<bold>.+?)\*\*|`(?<code>[^`]+)`|__(?<plain>.+?)__|~~(?<plain>.+?)~~|\|\|(?<plain>.+?)\|\|", RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    [GeneratedRegex(@"^#{1,3}\s+(?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Header();

    [GeneratedRegex(@"^-#\s+(?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Subtext();

    [GeneratedRegex(@"^>{1,3}\s?(?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Quote();

    [GeneratedRegex(@"^(#{1,3}\s+|-#\s+|>{1,3}\s?)", RegexOptions.CultureInvariant)]
    private static partial Regex LinePrefix();
}
