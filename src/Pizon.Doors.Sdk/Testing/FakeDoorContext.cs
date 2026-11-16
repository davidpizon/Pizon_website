using System.Text;
using Pizon.Doors.Sdk.Text;

namespace Pizon.Doors.Sdk.Testing;

/// <summary>
/// A scripted <see cref="IDoorContext"/> for door unit tests: queue keys and lines, run the door, then
/// inspect what it wrote.
/// </summary>
public sealed class FakeDoorContext : IDoorContext
{
    private readonly Queue<string?> input = new();
    private readonly StringBuilder output = new();
    private readonly Dictionary<string, string> assets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeDoorContext"/> class.
    /// </summary>
    /// <param name="caller">The caller; defaults to a signed-in tester.</param>
    /// <param name="seed">The random seed, so runs are repeatable.</param>
    public FakeDoorContext(DoorCaller? caller = null, int seed = 42)
    {
        Caller = caller ?? new DoorCaller("TESTER", true, 10);
        Random = new Random(seed);
    }

    /// <inheritdoc />
    public DoorCaller Caller { get; }

    /// <inheritdoc />
    public Random Random { get; }

    /// <inheritdoc />
    public DateOnly Today { get; set; } = new(2026, 11, 15);

    /// <summary>Gets the in-memory score board.</summary>
    public InMemoryDoorScores ScoreBoard { get; } = new();

    /// <inheritdoc />
    public IDoorScores Scores => ScoreBoard;

    /// <summary>Gets the in-memory storage.</summary>
    public InMemoryDoorStorage StorageData { get; } = new();

    /// <inheritdoc />
    public IDoorStorage Storage => StorageData;

    /// <summary>Gets everything written so far, ANSI included.</summary>
    public string Output => output.ToString();

    /// <summary>Gets everything written so far with escape sequences removed.</summary>
    public string PlainOutput => PipeCodes.StripAnsi(Output);

    /// <summary>Gets the number of times the screen was cleared.</summary>
    public int ClearCount { get; private set; }

    /// <summary>Gets the animation paths the door asked to play.</summary>
    public List<string> PlayedAnimations { get; } = [];

    /// <summary>Gets the total delay requested, for checking animation pacing.</summary>
    public TimeSpan TotalDelay { get; private set; }

    /// <summary>
    /// Queues keys to be returned by <see cref="ReadKeyAsync"/>.
    /// </summary>
    /// <param name="keys">The keys, in order.</param>
    /// <returns>This context, for chaining.</returns>
    public FakeDoorContext QueueKeys(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys)
        {
            input.Enqueue(key);
        }

        return this;
    }

    /// <summary>
    /// Queues a line to be returned by <see cref="ReadLineAsync"/>; <see langword="null"/> simulates Escape.
    /// </summary>
    /// <param name="line">The line text.</param>
    /// <returns>This context, for chaining.</returns>
    public FakeDoorContext QueueLine(string? line)
    {
        input.Enqueue(line);
        return this;
    }

    /// <summary>
    /// Adds a door asset that <see cref="LoadTextAsync"/> can return.
    /// </summary>
    /// <param name="path">The asset path.</param>
    /// <param name="text">The asset text.</param>
    /// <returns>This context, for chaining.</returns>
    public FakeDoorContext WithAsset(string path, string text)
    {
        assets[path] = text;
        return this;
    }

    /// <inheritdoc />
    public Task WriteAsync(string ansi, CancellationToken cancellationToken = default)
    {
        output.Append(ansi);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ClearCount++;
        output.Append(Ansi.ClearScreen);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> ReadKeyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Next() ?? DoorKeys.Escape);

    /// <inheritdoc />
    public Task<string?> ReadLineAsync(int maxLength, CancellationToken cancellationToken = default)
    {
        var line = Next();
        return Task.FromResult(line is null ? null : line.Length > maxLength ? line[..maxLength] : line);
    }

    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        TotalDelay += delay;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> PlayAnimationAsync(string path, CancellationToken cancellationToken = default)
    {
        PlayedAnimations.Add(path);
        return Task.FromResult(assets.ContainsKey(path));
    }

    /// <inheritdoc />
    public Task<string?> LoadTextAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(assets.TryGetValue(path, out var text) ? text : null);

    private string? Next() =>
        input.Count > 0
            ? input.Dequeue()
            : throw new InvalidOperationException("The door asked for more input than the test scripted.");
}

/// <summary>
/// An in-memory <see cref="IDoorScores"/> for tests.
/// </summary>
public sealed class InMemoryDoorScores : IDoorScores
{
    /// <summary>Gets every score submitted, in order.</summary>
    public List<long> Submitted { get; } = [];

    /// <summary>Gets or sets the board returned by <see cref="TopAsync"/>.</summary>
    public IReadOnlyList<ScoreEntry> Board { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether submissions are accepted.</summary>
    public bool Accept { get; set; } = true;

    /// <inheritdoc />
    public Task<bool> SubmitAsync(long score, CancellationToken cancellationToken = default)
    {
        Submitted.Add(score);
        return Task.FromResult(Accept);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScoreEntry>> TopAsync(CancellationToken cancellationToken = default) => Task.FromResult(Board);
}

/// <summary>
/// An in-memory <see cref="IDoorStorage"/> for tests.
/// </summary>
public sealed class InMemoryDoorStorage : IDoorStorage
{
    /// <summary>Gets the stored values.</summary>
    public Dictionary<string, string> Values { get; } = [];

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);

    /// <inheritdoc />
    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }
}
