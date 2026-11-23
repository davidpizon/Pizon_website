using System.Globalization;

namespace Pizon.Screens.Generator;

/// <summary>
/// Reads an animation's sidecar, <c>&lt;name&gt;.anim.json</c> next to the <c>.dur</c> file, which names
/// ranges of frames a page can choose between: <c>{ "ranges": { "idle": [0, 7], "busy": [8, 15] } }</c>
/// (first and last frame, counting from 0).
/// </summary>
internal static class AnimationSettings
{
    /// <summary>
    /// Reads the named ranges.
    /// </summary>
    /// <param name="json">The sidecar's text.</param>
    /// <param name="frameCount">How many frames the art has.</param>
    /// <returns>The ranges by name, in the order written.</returns>
    /// <exception cref="DurException">The sidecar is broken; the message says why.</exception>
    public static List<KeyValuePair<string, (int First, int Last)>> Ranges(string json, int frameCount)
    {
        Dictionary<string, object?>? root;
        try
        {
            root = MiniJson.Parse(json) as Dictionary<string, object?>;
        }
        catch (FormatException ex)
        {
            throw Bad("The .anim.json file isn't valid JSON: " + ex.Message);
        }

        if (root is null)
        {
            throw Bad("The .anim.json file must hold an object.");
        }

        var unknown = root.Keys.FirstOrDefault(k => k != "ranges");
        if (unknown is not null)
        {
            throw Bad("The .anim.json file has an unknown setting '" + unknown + "'; only \"ranges\" is read.");
        }

        var result = new List<KeyValuePair<string, (int First, int Last)>>();
        if (!root.TryGetValue("ranges", out var r) || r is null)
        {
            return result;
        }

        if (r is not Dictionary<string, object?> ranges)
        {
            throw Bad("\"ranges\" must be an object of name: [first, last].");
        }

        foreach (var pair in ranges)
        {
            if (!SlotScanner.IsRangeName(pair.Key))
            {
                throw Bad("The range name '" + pair.Key + "' must be lowercase letters, digits, '-' or '_', starting with a letter.");
            }

            if (pair.Value is not List<object?> { Count: 2 } bounds || bounds[0] is not double first || bounds[1] is not double last
                || first != Math.Floor(first) || last != Math.Floor(last) || first < 0 || last < first || last >= frameCount)
            {
                throw Bad(string.Format(CultureInfo.InvariantCulture, "The range '{0}' must be [first, last] with 0 <= first <= last < {1} (the frame count).", pair.Key, frameCount));
            }

            if (result.Any(x => SlotScanner.RangeName(x.Key) == SlotScanner.RangeName(pair.Key)))
            {
                throw Bad("The ranges '" + result.First(x => SlotScanner.RangeName(x.Key) == SlotScanner.RangeName(pair.Key)).Key + "' and '" + pair.Key + "' would get the same C# name.");
            }

            result.Add(new KeyValuePair<string, (int First, int Last)>(pair.Key, ((int)first, (int)last)));
        }

        return result;
    }

    private static DurException Bad(string message) => new(DurProblem.Animated, message);
}
