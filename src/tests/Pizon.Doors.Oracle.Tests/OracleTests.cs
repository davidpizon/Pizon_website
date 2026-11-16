using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Testing;
using Pizon.Doors.Sdk.Text;

namespace Pizon.Doors.Oracle.Tests;

/// <summary>
/// Tests for the fortune book: parsing, seeding and picking.
/// </summary>
public sealed class FortuneBookTests
{
    private static readonly DateOnly Day = new(2026, 11, 15);

    /// <summary>A valid file replaces the built-in fortunes; blanks and non-strings are dropped.</summary>
    [Fact]
    public void ParsesFortunes()
    {
        var book = FortuneBook.Parse("""{ "fortunes": ["  Yes. ", "", 5, "No."] }""");
        Assert.Equal(["Yes.", "No."], book.Fortunes);
    }

    /// <summary>Missing, empty or broken files fall back to the built-in fortunes.</summary>
    /// <param name="json">The file text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("{ broken")]
    [InlineData("""{ "other": [] }""")]
    [InlineData("""{ "fortunes": [""] }""")]
    public void FallsBackToDefault(string? json) => Assert.Same(FortuneBook.Default, FortuneBook.Parse(json));

    /// <summary>The shipped oracle.json parses to its own fortunes.</summary>
    [Fact]
    public void ShippedFileParses()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Pizon.Doors.Oracle", "wwwroot", "oracle.json");
        var book = FortuneBook.Parse(File.ReadAllText(path));
        Assert.NotSame(FortuneBook.Default, book);
        Assert.True(book.Fortunes.Count >= 50);
    }

    /// <summary>The same question on the same day gets the same answer, regardless of case and spacing.</summary>
    [Fact]
    public void SameQuestionSameDaySameAnswer()
    {
        var book = FortuneBook.Default;
        Assert.Equal(book.Pick("Will it work?", Day), book.Pick("  will   IT work ", Day));
        Assert.Equal(FortuneBook.Seed("a", Day), FortuneBook.Seed("A", Day));
        Assert.NotEqual(FortuneBook.Seed("a", Day), FortuneBook.Seed("a", Day.AddDays(1)));
        Assert.Equal("WILL IT WORK", FortuneBook.Normalize(" will it  work?! "));
    }
}

/// <summary>
/// Tests for the fortune card renderer.
/// </summary>
public sealed class FortuneCardTests
{
    /// <summary>Every style draws a card whose lines fit inside 80 columns, with the fortune inside.</summary>
    [Fact]
    public void RendersEveryStyle()
    {
        for (uint seed = 0; seed < (uint)FortuneCard.StyleCount * 4; seed++)
        {
            var card = FortuneCard.Render("The spirits say yes, but only after midnight and a long, long download.", seed);
            var plain = PipeCodes.StripAnsi(card);
            Assert.Contains("midnight", plain, StringComparison.Ordinal);
            Assert.All(plain.Split("\r\n"), line => Assert.True(line.Length <= 80, line));
        }
    }
}

/// <summary>
/// Tests for the door itself, driven through <see cref="FakeDoorContext"/>.
/// </summary>
public sealed class ModemOracleTests
{
    /// <summary>A member asks once: line noise, a card, one devotion point, then leaves.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task MemberAsksOnceAndLeaves()
    {
        var context = new FakeDoorContext().QueueLine("Will the BBS work?").QueueKeys("Q");
        var oracle = new ModemOracle();
        await oracle.RunAsync(context, CancellationToken.None);

        Assert.Equal("oracle", oracle.Id);
        Assert.Equal("THE MODEM ORACLE", oracle.Title);
        Assert.Contains("consulting the spirits", context.PlainOutput, StringComparison.Ordinal);
        Assert.Contains("carrier restored", context.PlainOutput, StringComparison.Ordinal);
        Assert.Equal([1L], context.ScoreBoard.Submitted);
        Assert.Equal(TimeSpan.FromMilliseconds(660), context.TotalDelay);
        Assert.Single(context.StorageData.Values);
    }

    /// <summary>Asking the same question twice in a day counts once; the board shows; ESC leaves.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RepeatQuestionCountsOnceAndShowsBoard()
    {
        var context = new FakeDoorContext()
            .QueueLine("same?").QueueKeys("x", "A")
            .QueueLine("SAME").QueueKeys("S", DoorKeys.Escape);
        context.ScoreBoard.Board = [new ScoreEntry(1, "Neo", 1), new ScoreEntry(2, "Trinity", 3)];
        await new ModemOracle().RunAsync(context, CancellationToken.None);

        Assert.Equal([1L], context.ScoreBoard.Submitted);
        Assert.Contains("MOST DEVOTED SEEKERS", context.PlainOutput, StringComparison.Ordinal);
        Assert.Contains("1 question\r\n", context.PlainOutput, StringComparison.Ordinal);
        Assert.Contains("3 questions", context.PlainOutput, StringComparison.Ordinal);
    }

    /// <summary>Guests get a fortune but no devotion points; Enter asks again; an empty question leaves.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GuestsAreNotRecorded()
    {
        var context = new FakeDoorContext(DoorCaller.Guest).QueueLine("hello").QueueKeys(DoorKeys.Enter).QueueLine(string.Empty);
        await new ModemOracle().RunAsync(context, CancellationToken.None);

        Assert.Empty(context.ScoreBoard.Submitted);
        Assert.Contains("Guests' devotion goes unrecorded", context.PlainOutput, StringComparison.Ordinal);
    }

    /// <summary>The door uses its own fortunes file when present, and ESC on the question leaves at once.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task UsesItsAssetAndLeavesOnEscape()
    {
        var context = new FakeDoorContext().WithAsset("oracle.json", """{ "fortunes": ["ONLY ANSWER"] }""").QueueLine("q").QueueKeys("Q");
        await new ModemOracle().RunAsync(context, CancellationToken.None);
        Assert.Contains("ONLY ANSWER", context.PlainOutput, StringComparison.Ordinal);

        var leaving = new FakeDoorContext().QueueLine(null);
        await new ModemOracle().RunAsync(leaving, CancellationToken.None);
        Assert.Empty(leaving.ScoreBoard.Submitted);
    }

    /// <summary>An empty board says so.</summary>
    [Fact]
    public void EmptyBoardSaysSo() =>
        Assert.Contains("No seekers yet today", PipeCodes.StripAnsi(ModemOracle.RenderBoard([])), StringComparison.Ordinal);
}
