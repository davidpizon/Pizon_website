using System.Reflection;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Screens;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Rendering;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// Tests for the compiled screen art and the <see cref="ScreenView"/> composer.
/// </summary>
public sealed class ScreenArtTests
{
    /// <summary>Gets every compiled art class's instance.</summary>
    public static TheoryData<string> ArtNames => [.. AllArt().Select(a => a.Name)];

    /// <summary>Every screen is drawn from compiled art, and none of it shows placeholder syntax to the caller.</summary>
    /// <param name="name">The art's name.</param>
    [Theory]
    [MemberData(nameof(ArtNames))]
    public void ArtNeverShowsPlaceholders(string name)
    {
        var art = AllArt().Single(a => a.Name == name);
        Assert.Equal(80, art.Columns);
        Assert.Equal(25, art.Rows);
        var view = new ScreenView(art);
        foreach (var slot in art.Fields)
        {
            view.Set(slot, "x");
        }

        var plain = PipeCodes.StripAnsi(view.Render());
        Assert.DoesNotMatch("@[A-Z?^\\[]", plain);
        Assert.DoesNotContain("..@", art.Text, StringComparison.Ordinal);
        Assert.All(art.Fields, f => Assert.InRange(f.Column + f.Width - 1, 1, 80));
    }

    /// <summary>There is a drawing for every screen that has one.</summary>
    [Fact]
    public void ArtCoversTheScreens() =>
        Assert.True(AllArt().Count() >= 21);

    /// <summary>The menu art has typed slots in the places the menu expects, and hotspots for its keys.</summary>
    [Fact]
    public void MenuArtHasSlotsAndHotspots()
    {
        Assert.Equal(new FieldSlot("HANDLE", SlotKind.Field, 15, 17, 16, 1, 14, 0, TextAlign.Left), MainMenuArt.Handle);
        Assert.Equal(MainMenuArt.Instance.Hotspots.Single(h => h.Key == "P").Column, MainMenuArt.ItemAccount.Column);
        Assert.Same(MainMenuArt.Cursor, MainMenuArt.Instance.CursorSlot);
        Assert.Equal(MainMenuArt.Handle, MainMenuArt.Instance.Field("HANDLE"));
        Assert.Null(MainMenuArt.Instance.Field("NOPE"));
        Assert.Contains(MainMenuArt.Instance.Hotspots, h => h.Key == "1" && h.Row == 6);
        Assert.Contains("MAIN MENU", MainMenuArt.Instance.Text, StringComparison.Ordinal);
        Assert.Equal('[', MainMenuArt.Instance.CharAt(6, 7));
        Assert.Equal(14, MainMenuArt.Instance.ForegroundAt(6, 7));
        Assert.Equal(0, MainMenuArt.Instance.BackgroundAt(6, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => MainMenuArt.Instance.CharAt(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MainMenuArt.Instance.CharAt(26, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MainMenuArt.Instance.CharAt(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MainMenuArt.Instance.CharAt(1, 81));
    }

    /// <summary>The animation slot and input slots come through with their kinds.</summary>
    [Fact]
    public void SlotKindsComeThrough()
    {
        Assert.Equal(SlotKind.Animation, BulletinCamArt.Cam.Kind);
        Assert.Equal((4, 54, 24, 12), (BulletinCamArt.Cam.Row, BulletinCamArt.Cam.Column, BulletinCamArt.Cam.Width, BulletinCamArt.Cam.Height));
        Assert.Equal(SlotKind.Input, PageArt.Message.Kind);
        Assert.Equal(TextAlign.Center, WelcomeArt.Tagline.Align);
        Assert.Equal(TextAlign.Right, WelcomeArt.Node.Align);
        Assert.Equal(17, BulletinArt.Body.Height);
    }

    /// <summary>Rendering clears, paints rows with hotspots, writes bound values and parks the cursor.</summary>
    [Fact]
    public void RendersFinishedAnsi()
    {
        var view = new ScreenView(MainMenuArt.Instance).Set(MainMenuArt.Handle, "Neo").Set(MainMenuArt.Level, "10");
        var ansi = view.Render();
        Assert.StartsWith(Ui.Cls, ansi, StringComparison.Ordinal);
        Assert.Contains(Ansi.HotspotStart("1"), ansi, StringComparison.Ordinal);
        Assert.Contains(Ansi.MoveTo(15, 17) + Ansi.Fg(14) + Ansi.Bg(0) + "Neo", ansi, StringComparison.Ordinal);
        Assert.EndsWith(Ansi.MoveTo(17, 17) + Ansi.Fg(7) + Ansi.Bg(0), ansi, StringComparison.Ordinal);
        Assert.Contains("Connected as", MainMenuArt.Instance.Text, StringComparison.Ordinal);
        Assert.Equal(["Neo"], view.Get(MainMenuArt.Handle));
        Assert.Empty(view.Get(MainMenuArt.Online));
    }

    /// <summary>Values are clipped, wrapped and aligned inside their slot.</summary>
    [Fact]
    public void LaysValuesOutInTheirSlot()
    {
        var view = new ScreenView(WelcomeArt.Instance);
        view.Set(WelcomeArt.Sysop, new string('x', 40));
        Assert.Equal(18, PipeCodes.VisibleLength(view.Get(WelcomeArt.Sysop)[0]));
        Assert.EndsWith("...", view.Get(WelcomeArt.Sysop)[0], StringComparison.Ordinal);

        var field = view.Set(WelcomeArt.Node, "7").RenderField(WelcomeArt.Node);
        Assert.Contains(Ansi.MoveTo(WelcomeArt.Node.Row, WelcomeArt.Node.Column), field, StringComparison.Ordinal);
        Assert.Contains("     7", PipeCodes.StripAnsi(field), StringComparison.Ordinal);

        var page = new ScreenView(PageArt.Instance).Set(PageArt.Status, string.Join(' ', Enumerable.Repeat("word", 60)));
        Assert.Equal(2, page.Get(PageArt.Status).Count);
        Assert.All(page.Get(PageArt.Status), l => Assert.True(PipeCodes.VisibleLength(l) <= 76));

        var list = new ScreenView(BulletinsArt.Instance).SetLines(BulletinsArt.List, Enumerable.Repeat("line", 40));
        Assert.Equal(12, list.Get(BulletinsArt.List).Count);
    }

    /// <summary>Slots from other art are rejected, so a page can only bind its own placeholders.</summary>
    [Fact]
    public void RejectsForeignSlots()
    {
        var view = new ScreenView(ConnectArt.Instance);
        Assert.Throws<ArgumentException>(() => view.Set(MainMenuArt.Handle, "x"));
        Assert.Throws<ArgumentNullException>(() => view.Set(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => new ScreenView(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.MoveTo(ConnectArt.Sound, 1));
        Assert.Equal(Ansi.MoveTo(8, ConnectArt.Sound.Column) + Ansi.Fg(11) + Ansi.Bg(0), view.MoveTo(ConnectArt.Sound));
    }

    /// <summary>Paging shows a block at a time with a "more" prompt, and stops on Q or Escape.</summary>
    /// <param name="stop">The key that stops paging.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData("q")]
    [InlineData(DoorKeys.Escape)]
    public async Task PagesWithinTheBlock(string stop)
    {
        var lines = Enumerable.Range(1, 40).Select(i => $"line {i}").ToList();
        var all = new FakeTerminal().Keys(DoorKeys.Enter, DoorKeys.Enter);
        var view = new ScreenView(BulletinArt.Instance);
        Assert.True(await view.PageAsync(all, BulletinArt.Body, BulletinArt.More, lines, CancellationToken.None));
        Assert.Contains("line 40", all.Plain, StringComparison.Ordinal);
        Assert.Contains("-- more --", all.Plain, StringComparison.Ordinal);
        Assert.Empty(view.Get(BulletinArt.More)[0]);

        var early = new FakeTerminal().Keys(stop);
        Assert.False(await view.PageAsync(early, BulletinArt.Body, BulletinArt.More, lines, CancellationToken.None));
        Assert.DoesNotContain("line 18", early.Plain, StringComparison.Ordinal);
    }

    /// <summary>The any-key prompt is shown at the cursor slot.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PromptsAtTheCursor()
    {
        var t = new FakeTerminal().Keys("x");
        Assert.Equal("x", await new ScreenView(NotFoundArt.Instance).PressAnyKeyAsync(t, CancellationToken.None));
        Assert.StartsWith(Ansi.MoveTo(NotFoundArt.Cursor.Row, NotFoundArt.Cursor.Column), t.Output, StringComparison.Ordinal);
        Assert.Contains("press any key", t.Plain, StringComparison.Ordinal);
    }

    private static IEnumerable<ScreenArt> AllArt() =>
        typeof(ScreenArt).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ScreenArt)))
            .Select(t => (ScreenArt)t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!)
            .OrderBy(a => a.Name, StringComparer.Ordinal);
}
