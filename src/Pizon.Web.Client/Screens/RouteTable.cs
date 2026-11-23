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
/// A screen's address in the browser.
/// </summary>
/// <param name="Screen">The screen.</param>
/// <param name="Path">The path, starting with <c>/</c> (lower case, no trailing slash).</param>
/// <param name="Title">The browser tab title (before " - PIZON BBS").</param>
/// <param name="Access">Who may open it from a link.</param>
/// <param name="Nested">
/// Whether addresses under the path belong to the same screen too (<c>/bulletins/2</c> is still the
/// bulletins screen); its pages tell them apart.
/// </param>
public sealed record ScreenRoute(ScreenId Screen, string Path, string Title, ScreenAccess Access, bool Nested = false);

/// <summary>
/// Maps screens to browser addresses and back, and decides what a caller sees when they open one.
/// Screens without a route (the handshake, the banner, sign-in, logout) keep the current address, so
/// no link can start them; in particular, opening a link never changes anything on the server.
/// </summary>
public static class RouteTable
{
    /// <summary>The site name added to every tab title.</summary>
    public const string SiteName = "PIZON BBS";

    /// <summary>
    /// Gets every routed screen. Every path here must also be an <c>@page</c>: on a screen page in
    /// <c>Screens/Pages</c>, or on <c>Pages.Home</c> for screens that are still legacy <see cref="IScreen"/> classes.
    /// </summary>
    public static IReadOnlyList<ScreenRoute> All { get; } =
    [
        new(ScreenId.Connect, "/", "Dial in", ScreenAccess.Public),
        new(ScreenId.Login, "/login", "Log in", ScreenAccess.Public),
        new(ScreenId.MainMenu, "/menu", "Main menu", ScreenAccess.Public),
        new(ScreenId.Bulletins, "/bulletins", "System bulletins", ScreenAccess.Public, Nested: true),
        new(ScreenId.MessageBoard, "/board", "Message boards", ScreenAccess.Public),
        new(ScreenId.BoardNew, "/board/new", "New message", ScreenAccess.Member),
        new(ScreenId.BoardReply, "/board/reply", "Reply to a message", ScreenAccess.Member),
        new(ScreenId.FileAreas, "/files", "File areas", ScreenAccess.Public, Nested: true),
        new(ScreenId.Doors, "/doors", "Door games", ScreenAccess.Public),
        new(ScreenId.UserList, "/users", "User list & hall of fame", ScreenAccess.Public),
        new(ScreenId.RipCard, "/rip", "RIP welcome card", ScreenAccess.Public),
        new(ScreenId.Settings, "/settings", "Terminal settings", ScreenAccess.Public),
        new(ScreenId.Goodbye, "/goodbye", "Goodbye", ScreenAccess.Public),
        new(ScreenId.PageSysop, "/page", "Page the SysOp", ScreenAccess.Member),
        new(ScreenId.ChangeHandle, "/handle", "Change handle", ScreenAccess.Member),
        new(ScreenId.SysopConsole, "/sysop", "SysOp console", ScreenAccess.Sysop),
    ];

    /// <summary>
    /// Gets a screen's route.
    /// </summary>
    /// <param name="screen">The screen.</param>
    /// <returns>The route, or <see langword="null"/> when the screen keeps the current address.</returns>
    public static ScreenRoute? For(ScreenId screen) => All.FirstOrDefault(r => r.Screen == screen);

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
    /// Gets a value indicating whether a screen is part of the way in (dial-up to login). Moves away from
    /// these replace the browser history entry, so Back from the main menu doesn't redial.
    /// </summary>
    /// <param name="screen">The screen.</param>
    /// <returns><see langword="true"/> for entrance screens.</returns>
    public static bool IsEntrance(ScreenId screen) =>
        screen is ScreenId.Connect or ScreenId.Handshake or ScreenId.Welcome or ScreenId.Login or ScreenId.NewUserHandle;

    /// <summary>
    /// Decides which screen a caller actually gets when they ask for one. Callers who haven't logged on
    /// yet this call are sent to the login screen (which comes back afterwards); a signed-on caller who
    /// isn't the SysOp gets "file not found" for SysOp screens. Member screens still check for guests
    /// themselves, so a guest who chose GUEST at login sees their ACCESS DENIED message.
    /// </summary>
    /// <param name="target">The screen asked for.</param>
    /// <param name="session">The call.</param>
    /// <returns>The screen to run.</returns>
    public static ScreenId Guard(ScreenId target, BbsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var route = For(target);
        if (route is null || route.Access == ScreenAccess.Public)
        {
            return target;
        }

        var allowed = route.Access == ScreenAccess.Sysop ? session.IsSysop : session.IsMember;
        if (allowed)
        {
            return target;
        }

        if (!session.SignedOn)
        {
            session.ReturnTo = route.Path;
            return ScreenId.Login;
        }

        return route.Access == ScreenAccess.Sysop ? ScreenId.NotFound : target;
    }
}
