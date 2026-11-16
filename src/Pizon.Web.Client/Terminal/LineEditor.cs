using System.Text;
using Pizon.Doors.Sdk;

namespace Pizon.Web.Client.Terminal;

/// <summary>
/// Classic BBS line input: characters echo as typed, Backspace erases, Enter accepts, Escape cancels.
/// </summary>
public static class LineEditor
{
    /// <summary>
    /// Reads one line.
    /// </summary>
    /// <param name="terminal">The terminal.</param>
    /// <param name="maxLength">The most characters accepted.</param>
    /// <param name="initial">Text pre-filled for editing (shown and editable).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The text, or <see langword="null"/> when the caller pressed Escape.</returns>
    public static async Task<string?> ReadLineAsync(ITerminal terminal, int maxLength, string initial = "", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(initial);
        var text = new StringBuilder(initial.Length > maxLength ? initial[..maxLength] : initial);
        await terminal.SetLineModeAsync(true);
        try
        {
            if (text.Length > 0)
            {
                await terminal.WriteAsync(text.ToString(), cancellationToken);
            }

            while (true)
            {
                var key = await terminal.ReadKeyAsync(cancellationToken);
                switch (key)
                {
                    case DoorKeys.Enter:
                        return text.ToString();
                    case DoorKeys.Escape:
                        return null;
                    case DoorKeys.Backspace when text.Length > 0:
                        text.Length--;
                        await terminal.WriteAsync("\b \b", cancellationToken);
                        break;
                    default:
                        if (DoorKeys.IsPrintable(key) && text.Length < maxLength)
                        {
                            text.Append(key);
                            await terminal.WriteAsync(key, cancellationToken);
                        }

                        break;
                }
            }
        }
        finally
        {
            await terminal.SetLineModeAsync(false);
        }
    }

    /// <summary>
    /// Reads a multi-line message the 1990s way: one line at a time, a blank line (or the length limit) ends it.
    /// </summary>
    /// <param name="terminal">The terminal.</param>
    /// <param name="maxLines">The most lines accepted.</param>
    /// <param name="lineWidth">The width of each line.</param>
    /// <param name="maxTotal">The most characters in total.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The message (lines joined with newlines), or <see langword="null"/> when cancelled with Escape.</returns>
    public static async Task<string?> ReadMessageAsync(ITerminal terminal, int maxLines, int lineWidth, int maxTotal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        var lines = new List<string>();
        while (lines.Count < maxLines)
        {
            var used = lines.Sum(l => l.Length + 1);
            var room = Math.Min(lineWidth, maxTotal - used);
            if (room <= 0)
            {
                break;
            }

            await terminal.WriteAsync($"\u001b[38;5;8m{lines.Count + 1,2}:\u001b[38;5;7m ", cancellationToken);
            var line = await ReadLineAsync(terminal, room, string.Empty, cancellationToken);
            await terminal.WriteAsync("\r\n", cancellationToken);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            lines.Add(line);
        }

        return string.Join('\n', lines).Trim();
    }
}
