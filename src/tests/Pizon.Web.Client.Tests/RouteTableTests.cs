using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// Tests for screen addresses and who may open them.
/// </summary>
public sealed class RouteTableTests
{
    /// <summary>Only plain site-relative paths of known routes resolve; queries, fragments, case and a trailing slash are ignored.</summary>
    /// <param name="path">The path.</param>
    /// <param name="expected">The route's path, or null when the path must not resolve.</param>
    [Theory]
    [InlineData("/BOARD/?x=1#top", "/board")]
    [InlineData("/", "/")]
    [InlineData("/sysop", "/sysop")]
    [InlineData("/sysop/queue", "/sysop")]
    [InlineData("/bulletins/2", "/bulletins")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("menu", null)]
    [InlineData("//evil.example/menu", null)]
    [InlineData("/\\evil.example", null)]
    [InlineData("/nowhere", null)]
    [InlineData("/menu/2", null)]
    public void ResolvesOnlyKnownLocalPaths(string? path, string? expected)
    {
        var found = RouteTable.TryResolve(path, out var route);
        Assert.Equal(expected is not null, found);
        if (expected is not null)
        {
            Assert.Equal(expected, route.Path);
        }
    }

    /// <summary>Every route has one unique, normalized path, and tab titles carry the site name.</summary>
    [Fact]
    public void RoutesAreUniqueAndNormalized()
    {
        Assert.Equal(RouteTable.All.Count, RouteTable.All.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(RouteTable.All, r => Assert.Equal(RouteTable.Normalize(r.Path.ToLowerInvariant()), r.Path));
        Assert.Equal("/", RouteTable.Normalize(string.Empty));
        Assert.Equal("Log in - PIZON BBS", RouteTable.Tab("Log in"));
    }

    /// <summary>
    /// Public screens open for anyone; callers who haven't logged on log on first (remembering the address);
    /// logged-on guests reach member screens (which deny them) but never see the SysOp console.
    /// </summary>
    [Fact]
    public void GuardsScreens()
    {
        static ScreenRoute Route(string path) => RouteTable.All.Single(r => r.Path == path);
        var guest = new BbsSession();
        Assert.Equal(RouteDecision.Show, RouteTable.Guard(Route("/bulletins"), "/bulletins/2", guest));
        Assert.Null(guest.ReturnTo);
        Assert.Equal(RouteDecision.LogOnFirst, RouteTable.Guard(Route("/sysop"), "/sysop/queue", guest));
        Assert.Equal("/sysop/queue", guest.ReturnTo);

        guest.SignedOn = true;
        Assert.Equal(RouteDecision.Show, RouteTable.Guard(Route("/page"), "/page", guest));
        Assert.Equal(RouteDecision.NotFound, RouteTable.Guard(Route("/sysop"), "/sysop", guest));

        var sysop = new BbsSession { Me = new MeResponse(true, "SysOp1", "SysOp1", 255, true, true, true), SignedOn = true };
        Assert.Equal(RouteDecision.Show, RouteTable.Guard(Route("/sysop"), "/sysop", sysop));
        Assert.Equal(RouteDecision.Show, RouteTable.Guard(Route("/handle"), "/handle", sysop));
        Assert.Throws<ArgumentNullException>(() => RouteTable.Guard(null!, "/", sysop));
        Assert.Throws<ArgumentNullException>(() => RouteTable.Guard(Route("/"), "/", null!));
    }
}
