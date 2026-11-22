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
    /// <param name="expected">The screen, or null when the path must not resolve.</param>
    [Theory]
    [InlineData("/BOARD/?x=1#top", ScreenId.MessageBoard)]
    [InlineData("/", ScreenId.Connect)]
    [InlineData("/sysop", ScreenId.SysopConsole)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("menu", null)]
    [InlineData("//evil.example/menu", null)]
    [InlineData("/\\evil.example", null)]
    [InlineData("/nowhere", null)]
    public void ResolvesOnlyKnownLocalPaths(string? path, ScreenId? expected)
    {
        var found = RouteTable.TryResolve(path, out var route);
        Assert.Equal(expected is not null, found);
        if (expected is { } screen)
        {
            Assert.Equal(screen, route.Screen);
        }
    }

    /// <summary>Every routed screen has one unique, normalized path; screens that change things have none.</summary>
    [Fact]
    public void RoutesAreUniqueAndSafe()
    {
        Assert.Equal(RouteTable.All.Count, RouteTable.All.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(RouteTable.All.Count, RouteTable.All.Select(r => r.Screen).Distinct().Count());
        Assert.All(RouteTable.All, r => Assert.Equal(RouteTable.Normalize(r.Path.ToLowerInvariant()), r.Path));
        Assert.Null(RouteTable.For(ScreenId.Logout));
        Assert.Null(RouteTable.For(ScreenId.SignIn));
        Assert.Null(RouteTable.For(ScreenId.Handshake));
        Assert.Equal("/", RouteTable.Normalize(string.Empty));
        Assert.True(RouteTable.IsEntrance(ScreenId.Login));
        Assert.False(RouteTable.IsEntrance(ScreenId.MainMenu));
    }

    /// <summary>
    /// Public screens open for anyone; callers who haven't logged on go to login (remembering the screen);
    /// logged-on guests reach member screens (which deny them) but never see the SysOp console.
    /// </summary>
    [Fact]
    public void GuardsScreens()
    {
        var guest = new BbsSession();
        Assert.Equal(ScreenId.Bulletins, RouteTable.Guard(ScreenId.Bulletins, guest));
        Assert.Equal(ScreenId.Handshake, RouteTable.Guard(ScreenId.Handshake, guest));
        Assert.Equal(ScreenId.Login, RouteTable.Guard(ScreenId.SysopConsole, guest));
        Assert.Equal("/sysop", guest.ReturnTo);

        guest.SignedOn = true;
        Assert.Equal(ScreenId.PageSysop, RouteTable.Guard(ScreenId.PageSysop, guest));
        Assert.Equal(ScreenId.NotFound, RouteTable.Guard(ScreenId.SysopConsole, guest));

        var sysop = new BbsSession { Me = new MeResponse(true, "SysOp1", "SysOp1", 255, true, true, true), SignedOn = true };
        Assert.Equal(ScreenId.SysopConsole, RouteTable.Guard(ScreenId.SysopConsole, sysop));
        Assert.Equal(ScreenId.ChangeHandle, RouteTable.Guard(ScreenId.ChangeHandle, sysop));
    }
}
