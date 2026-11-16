using System.Globalization;
using System.Text;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;

namespace Pizon.Doors.Oracle;

/// <summary>
/// The Modem Oracle: ask a question, listen to the line noise, receive a fortune card.
/// Signed-in callers earn a place on the "Most Devoted Seekers" board (one point per new question per day).
/// </summary>
public sealed class ModemOracle : IDoor
{
    private const string NoiseChars = "~}{#@$%^&*+=<>/\\|;:0123456789ABCDEF";
    private const int MaxQuestionLength = 60;

    /// <inheritdoc />
    public string Id => "oracle";

    /// <inheritdoc />
    public string Title => "THE MODEM ORACLE";

    /// <inheritdoc />
    public async Task RunAsync(IDoorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var book = FortuneBook.Parse(await context.LoadTextAsync("oracle.json", cancellationToken));
        await context.ClearAsync(cancellationToken);
        await context.WriteAsync(PipeCodes.ToAnsi(TitleCard), cancellationToken);

        while (true)
        {
            await context.WriteAsync(PipeCodes.ToAnsi("\r\n|11Ask thy question, mortal |08(ENTER to leave)|07: "), cancellationToken);
            var question = await context.ReadLineAsync(MaxQuestionLength, cancellationToken);
            if (string.IsNullOrWhiteSpace(question))
            {
                return;
            }

            await LineNoiseAsync(context, cancellationToken);
            var seed = FortuneBook.Seed(question, context.Today);
            await context.WriteAsync(FortuneCard.Render(book.Pick(question, context.Today), seed), cancellationToken);
            await RecordDevotionAsync(context, question, cancellationToken);

            if (!await AfterFortuneMenuAsync(context, cancellationToken))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Renders the "Most Devoted Seekers" board.
    /// </summary>
    /// <param name="entries">Today's board.</param>
    /// <returns>The board as ANSI text.</returns>
    public static string RenderBoard(IReadOnlyList<ScoreEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sb = new StringBuilder();
        sb.Append(PipeCodes.ToAnsi("\r\n|13  ═══ MOST DEVOTED SEEKERS (TODAY) ═══|07\r\n"));
        if (entries.Count == 0)
        {
            sb.Append(PipeCodes.ToAnsi("|08  No seekers yet today. The spirits are lonely.|07\r\n"));
            return sb.ToString();
        }

        foreach (var e in entries)
        {
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"  |14{e.Rank,2}.|07 {TextWrap.Fit(e.Handle, 16)} |11{e.Score,4}|08 question{(e.Score == 1 ? string.Empty : "s")}|07\r\n");
            sb.Append(PipeCodes.ToAnsi(line));
        }

        return sb.ToString();
    }

    private static string TitleCard =>
        "|#093  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·\r\n" +
        "|#201              ╔╦╗╦ ╦╔═╗  ╔╦╗╔═╗╔╦╗╔═╗╔╦╗  ╔═╗╦═╗╔═╗╔═╗╦  ╔═╗\r\n" +
        "|#165               ║ ╠═╣║╣   ║║║║ ║ ║║║╣ ║║║  ║ ║╠╦╝╠═╣║  ║  ║╣\r\n" +
        "|#129               ╩ ╩ ╩╚═╝  ╩ ╩╚═╝═╩╝╚═╝╩ ╩  ╚═╝╩╚═╩ ╩╚═╝╩═╝╚═╝\r\n" +
        "|#093  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·  ·\r\n" +
        "|08         The spirits of the phone line await. Speak plainly; they bill by the byte.|07\r\n";

    private static async Task LineNoiseAsync(IDoorContext context, CancellationToken cancellationToken)
    {
        await context.WriteAsync(PipeCodes.ToAnsi("\r\n|08consulting the spirits of the phone line...|07\r\n"), cancellationToken);
        for (var frame = 0; frame < 6; frame++)
        {
            var sb = new StringBuilder("\r");
            for (var i = 0; i < 48; i++)
            {
                sb.Append(Ansi.Fg(context.Random.Next(16, 232)))
                    .Append(NoiseChars[context.Random.Next(NoiseChars.Length)]);
            }

            await context.WriteAsync(sb.ToString(), cancellationToken);
            await context.DelayAsync(TimeSpan.FromMilliseconds(110), cancellationToken);
        }

        await context.WriteAsync("\r" + Ansi.EraseToEndOfLine + PipeCodes.ToAnsi("|08+++ATH0  ...carrier restored.|07\r\n\r\n"), cancellationToken);
    }

    private static async Task RecordDevotionAsync(IDoorContext context, string question, CancellationToken cancellationToken)
    {
        if (!context.Caller.IsAuthenticated)
        {
            await context.WriteAsync(PipeCodes.ToAnsi("|08  (Guests' devotion goes unrecorded. Sign in to join the Seekers board.)|07\r\n"), cancellationToken);
            return;
        }

        var key = "asked-" + context.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var hash = FortuneBook.Seed(question, context.Today).ToString(CultureInfo.InvariantCulture);
        var asked = (await context.Storage.GetAsync(key, cancellationToken) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (asked.Contains(hash, StringComparer.Ordinal))
        {
            return;
        }

        await context.Storage.SetAsync(key, string.Join(',', asked.Append(hash)), cancellationToken);
        await context.Scores.SubmitAsync(1, cancellationToken);
    }

    private static async Task<bool> AfterFortuneMenuAsync(IDoorContext context, CancellationToken cancellationToken)
    {
        while (true)
        {
            await context.WriteAsync(
                PipeCodes.ToAnsi("\r\n  |[A]|14[A]|07 Ask again|]   |[S]|14[S]|07 Seekers board|]   |[Q]|14[Q]|07 Leave the Oracle|]\r\n"),
                cancellationToken);
            var key = await context.ReadKeyAsync(cancellationToken);
            if (DoorKeys.Is(key, 'A') || key == DoorKeys.Enter)
            {
                return true;
            }

            if (DoorKeys.Is(key, 'S'))
            {
                await context.WriteAsync(RenderBoard(await context.Scores.TopAsync(cancellationToken)), cancellationToken);
                continue;
            }

            if (DoorKeys.Is(key, 'Q') || key == DoorKeys.Escape)
            {
                return false;
            }
        }
    }
}
