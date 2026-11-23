namespace Pizon.Web.Client.Content;

/// <summary>
/// <c>content/bulletins.json</c>: the SysOp's status line and the bulletins.
/// </summary>
public sealed class BulletinBoard
{
    /// <summary>Gets the SysOp's name for the status header.</summary>
    public string Sysop { get; init; } = "David Pizon";

    /// <summary>Gets the SysOp's current mood (status header).</summary>
    public string Mood { get; init; } = "Caffeinated";

    /// <summary>Gets the SysOp's current location (status header).</summary>
    public string Location { get; init; } = "Somewhere on the grid";

    /// <summary>Gets the tagline under the banner.</summary>
    public string Tagline { get; init; } = "A personal BBS since 2026 (spiritually since 1993)";

    /// <summary>Gets the bulletins in menu order.</summary>
    public IReadOnlyList<Bulletin> Bulletins { get; init; } = [];
}

/// <summary>
/// One bulletin.
/// </summary>
public sealed class Bulletin
{
    /// <summary>Gets the bulletin number shown in the list.</summary>
    public int Number { get; init; }

    /// <summary>Gets the title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the "last updated" date text.</summary>
    public string Updated { get; init; } = string.Empty;

    /// <summary>Gets the body lines (pipe codes allowed).</summary>
    public IReadOnlyList<string> Lines { get; init; } = [];

    /// <summary>Gets a value indicating whether the SysOp cam plays beside the text.</summary>
    public bool Cam { get; init; }
}

/// <summary>
/// <c>content/files.json</c>: the file areas (favorite links presented as a shareware directory).
/// </summary>
public sealed class FileLibrary
{
    /// <summary>Gets the areas.</summary>
    public IReadOnlyList<FileArea> Areas { get; init; } = [];
}

/// <summary>
/// One file area.
/// </summary>
public sealed class FileArea
{
    /// <summary>Gets the area name, e.g. <c>TOOLS</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the area description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the files.</summary>
    public IReadOnlyList<FileEntry> Files { get; init; } = [];
}

/// <summary>
/// One "file": an 8.3 name, a fake size, a short description and the real link.
/// </summary>
public sealed class FileEntry
{
    /// <summary>Gets the 8.3 file name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the fake size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>Gets the description (45 characters or fewer).</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the real URL opened when "downloaded".</summary>
    public Uri? Url { get; init; }
}

/// <summary>
/// <c>content/halloffame.json</c>: friends, influences and favorite creators for the user list.
/// </summary>
public sealed class HallOfFame
{
    /// <summary>Gets the entries.</summary>
    public IReadOnlyList<FameEntry> Entries { get; init; } = [];
}

/// <summary>
/// One Hall of Fame entry.
/// </summary>
public sealed class FameEntry
{
    /// <summary>Gets the name or handle.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Gets the (made-up) security clearance.</summary>
    public string Clearance { get; init; } = "LVL 100";

    /// <summary>Gets a short note.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>Gets an optional link.</summary>
    public Uri? Url { get; init; }
}
