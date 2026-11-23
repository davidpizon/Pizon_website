namespace Pizon.Web.Client.Screens;

/// <summary>
/// Who may open a routed screen straight from its address.
/// </summary>
public enum ScreenAccess
{
    /// <summary>Anyone, including guests.</summary>
    Public,

    /// <summary>Signed-in members (guests are sent to the login screen first).</summary>
    Member,

    /// <summary>The SysOp only (anyone else signed in sees "file not found").</summary>
    Sysop,
}

/// <summary>
/// What the host does with an address (see <see cref="RouteTable.Guard"/>).
/// </summary>
public enum RouteDecision
{
    /// <summary>Show the page.</summary>
    Show,

    /// <summary>Send the caller to the login screen first; they come back afterwards.</summary>
    LogOnFirst,

    /// <summary>Show "file not found" instead (a SysOp screen, for anyone else).</summary>
    NotFound,
}

/// <summary>
/// A screen page's address in the browser.
/// </summary>
/// <param name="Path">The path, starting with <c>/</c> (lower case, no trailing slash).</param>
/// <param name="Title">The browser tab title (before " - PIZON BBS").</param>
/// <param name="Access">Who may open it from a link.</param>
/// <param name="Nested">
/// Whether addresses under the path belong to the same route too (<c>/bulletins/2</c> is still under
/// <c>/bulletins</c>); its pages tell them apart.
/// </param>
public sealed record ScreenRoute(string Path, string Title, ScreenAccess Access, bool Nested = false);

/// <summary>
/// The screen pages' addresses, and what a caller sees when they open one. Everything without an address
/// (the handshake, signing in, logging out) runs inside a page, so no link can start it; in particular,
/// opening a link never changes anything on the server.
/// </summary>
public static class RouteTable
{
    /// <summary>The site name added to every tab title.</summary>
    public const string SiteName = "PIZON BBS";

    /// <summary>
    /// Gets every route. Every path here must also be an <c>@page</c> in <c>Screens/Pages</c>, with a
    /// matching <c>[BbsAccess]</c> (a test checks both).
    /// </summary>
    public static IReadOnlyList<ScreenRoute> All { get; } =
    [
        new("/", "Dial in", ScreenAccess.Public),
        new("/login", "Log in", ScreenAccess.Public),
        new("/menu", "Main menu", ScreenAccess.Public),
        new("/bulletins", "System bulletins", ScreenAccess.Public, Nested: true),
        new("/board", "Message boards", ScreenAccess.Public),
        new("/board/new", "New message", ScreenAccess.Member),
        new("/board/reply", "Reply to a message", ScreenAccess.Member),
        new("/files", "File areas", ScreenAccess.Public, Nested: true),
        new("/doors", "Door games", ScreenAccess.Public),
        new("/users", "User list & hall of fame", ScreenAccess.Public),
        new("/rip", "RIP welcome card", ScreenAccess.Public),
        new("/settings", "Terminal settings", ScreenAccess.Public),
        new("/goodbye", "Goodbye", ScreenAccess.Public),
        new("/page", "Page the SysOp", ScreenAccess.Member),
        new("/handle", "Change handle", ScreenAccess.Member),
        new("/handle/new", "Choose your handle", ScreenAccess.Public),
        new("/sysop", "SysOp console", ScreenAccess.Sysop, Nested: true),
    ];

    /// <summary>
    /// Builds a browser tab title.
    /// </summary>
    /// <param name="title">The screen's title.</param>
    /// <returns>"Title - PIZON BBS".</returns>
    public static string Tab(string title) => $"{title} - {SiteName}";

    /// <summary>
    /// Finds the route for a local path: its own route, or the nearest nested route above it. Only plain site-relative paths are accepted (never <c>//host</c>
    /// or <c>/\host</c>), so a <c>returnUrl</c> from a link can't send the caller anywhere else.
    /// </summary>
    /// <param name="path">The path, optionally with a query or fragment (ignored).</param>
    /// <param name="route">The route found.</param>
    /// <returns><see langword="true"/> when the path is a known route.</returns>
    public static bool TryResolve(string? path, out ScreenRoute route)
    {
        route = null!;
        if (path is not { Length: > 0 } || path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith("/\\", StringComparison.Ordinal))
        {
            return false;
        }

        var end = path.IndexOfAny(['?', '#']);
        var clean = Normalize(end < 0 ? path : path[..end]);
        var found = All.FirstOrDefault(r => string.Equals(r.Path, clean, StringComparison.OrdinalIgnoreCase)) ??
            All.Where(r => r.Nested && clean.StartsWith(r.Path + "/", StringComparison.OrdinalIgnoreCase)).MaxBy(r => r.Path.Length);
        if (found is null)
        {
            return false;
        }

        route = found;
        return true;
    }

    /// <summary>
    /// Normalizes a path for comparison: no trailing slash, and an empty path is <c>/</c>.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The normalized path.</returns>
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    /// <summary>
    /// Decides what a caller gets when they open an address. Callers who haven't logged on yet this call
    /// log on first (the login screen comes back to <paramref name="path"/>, noted in
    /// <see cref="BbsSession.ReturnTo"/>); a signed-on caller who isn't the SysOp gets "file not found" for
    /// SysOp screens. Member screens still check for guests themselves, so a guest who chose GUEST at login
    /// sees their ACCESS DENIED message.
    /// </summary>
    /// <param name="route">The route of the address.</param>
    /// <param name="path">The address's path (where to come back to).</param>
    /// <param name="session">The call.</param>
    /// <returns>What to do.</returns>
    public static RouteDecision Guard(ScreenRoute route, string path, BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(session);
        var allowed = route.Access switch
        {
            ScreenAccess.Public => true,
            ScreenAccess.Sysop => session.IsSysop,
            _ => session.IsMember,
        };
        if (allowed)
        {
            return RouteDecision.Show;
        }

        if (!session.SignedOn)
        {
            session.ReturnTo = path;
            return RouteDecision.LogOnFirst;
        }

        return route.Access == ScreenAccess.Sysop ? RouteDecision.NotFound : RouteDecision.Show;
    }
}
