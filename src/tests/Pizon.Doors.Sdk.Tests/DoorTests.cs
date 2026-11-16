using Pizon.Doors.Sdk.Testing;

namespace Pizon.Doors.Sdk.Tests;

/// <summary>
/// Tests for the door manifest.
/// </summary>
public sealed class DoorManifestTests
{
    private const string Valid = """
        // comments are allowed
        {
          "doors": [
            { "id": "oracle", "title": "Oracle", "assemblies": ["Pizon.Doors.Oracle.wasm"], "entryType": "X.Oracle",
              "scored": true, "scoreMode": "dailyCount", "maxScore": 50, },
            { "id": "off", "title": "Off", "assemblies": ["Off.wasm"], "entryType": "X.Off", "enabled": false }
          ]
        }
        """;

    /// <summary>A valid manifest parses with defaults and enums.</summary>
    [Fact]
    public void ParsesValidManifest()
    {
        var manifest = DoorManifest.Parse(Valid);
        var oracle = Assert.Single(manifest.EnabledDoors);
        Assert.Equal(ScoreMode.DailyCount, oracle.ScoreMode);
        Assert.Equal(50, oracle.MaxScore);
        Assert.Equal("HIGH SCORES", oracle.ScoreTitle);
        Assert.Equal("_content/Pizon.Doors.Oracle/", oracle.AssetBase);
        Assert.Same(oracle, manifest.Find("ORACLE"));
        Assert.Null(manifest.Find("nope"));
        Assert.Equal(ScoreMode.Max, manifest.Find("off")!.ScoreMode);
    }

    /// <summary>Broken JSON, missing fields and duplicate ids are rejected.</summary>
    /// <param name="json">The bad manifest.</param>
    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "doors": [ { "id": "a", "assemblies": ["a.wasm"] } ] }""")]
    [InlineData("""{ "doors": [ { "id": "a", "entryType": "A", "assemblies": [] } ] }""")]
    [InlineData("""{ "doors": [ { "id": "a", "entryType": "A", "assemblies": ["a.wasm"] }, { "id": "A", "entryType": "B", "assemblies": ["b.wasm"] } ] }""")]
    public void RejectsBadManifests(string json) => Assert.Throws<InvalidDataException>(() => DoorManifest.Parse(json));

    /// <summary>"null" parses to an empty manifest.</summary>
    [Fact]
    public void NullJsonIsEmpty() => Assert.Empty(DoorManifest.Parse("null").Doors);
}

/// <summary>
/// Tests for key helpers and the caller record.
/// </summary>
public sealed class DoorKeysTests
{
    /// <summary>Printable keys and letter matching.</summary>
    [Fact]
    public void ClassifiesKeys()
    {
        Assert.True(DoorKeys.IsPrintable("a"));
        Assert.False(DoorKeys.IsPrintable(DoorKeys.Enter));
        Assert.False(DoorKeys.IsPrintable("\t"));
        Assert.False(DoorKeys.IsPrintable(null));
        Assert.True(DoorKeys.Is("q", 'Q'));
        Assert.True(DoorKeys.Is("Q", 'q'));
        Assert.False(DoorKeys.Is("Quit", 'Q'));
        Assert.Equal(new DoorCaller("GUEST", false, 0), DoorCaller.Guest);
    }
}

/// <summary>
/// Tests for the fake door context used by door test projects.
/// </summary>
public sealed class FakeDoorContextTests
{
    /// <summary>Scripted input, output capture, assets, delays, scores and storage all work.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ScriptsADoorSession()
    {
        var context = new FakeDoorContext(new DoorCaller("Neo", true, 10))
            .QueueKeys("a")
            .QueueLine("a long line")
            .QueueLine(null)
            .WithAsset("intro.json", "{}");

        await context.WriteAsync("\u001b[38;5;14mhello");
        await context.ClearAsync();
        Assert.Equal("a", await context.ReadKeyAsync());
        Assert.Equal("a lo", await context.ReadLineAsync(4));
        Assert.Null(await context.ReadLineAsync(4));
        Assert.Throws<InvalidOperationException>(() => context.ReadKeyAsync().GetAwaiter().GetResult());
        await context.DelayAsync(TimeSpan.FromSeconds(2));
        Assert.True(await context.PlayAnimationAsync("intro.json"));
        Assert.False(await context.PlayAnimationAsync("missing.json"));
        Assert.Equal("{}", await context.LoadTextAsync("intro.json"));
        Assert.Null(await context.LoadTextAsync("missing.json"));

        Assert.StartsWith("hello", context.PlainOutput, StringComparison.Ordinal);
        Assert.Equal(1, context.ClearCount);
        Assert.Equal(TimeSpan.FromSeconds(2), context.TotalDelay);
        Assert.Equal(["intro.json", "missing.json"], context.PlayedAnimations);
        Assert.Equal("Neo", context.Caller.Handle);
        Assert.Equal(new DateOnly(2026, 11, 15), context.Today);
        Assert.InRange(context.Random.Next(10), 0, 9);
    }

    /// <summary>Queued null keys read as Escape, and the default caller is a signed-in tester.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NullKeyReadsAsEscape()
    {
        var context = new FakeDoorContext().QueueLine(null);
        Assert.Equal(DoorKeys.Escape, await context.ReadKeyAsync());
        Assert.Equal(new DoorCaller("TESTER", true, 10), context.Caller);
    }

    /// <summary>The in-memory score board and storage record what doors do.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ScoresAndStorage()
    {
        var context = new FakeDoorContext();
        Assert.True(await context.Scores.SubmitAsync(5));
        context.ScoreBoard.Accept = false;
        Assert.False(await context.Scores.SubmitAsync(6));
        Assert.Equal([5L, 6L], context.ScoreBoard.Submitted);
        context.ScoreBoard.Board = [new ScoreEntry(1, "Neo", 5)];
        Assert.Single(await context.Scores.TopAsync());

        Assert.Null(await context.Storage.GetAsync("k"));
        await context.Storage.SetAsync("k", "v");
        Assert.Equal("v", await context.Storage.GetAsync("k"));
        Assert.Equal("v", context.StorageData.Values["k"]);
    }
}
