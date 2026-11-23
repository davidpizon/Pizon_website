using Bunit;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Screens;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Components;
using Pizon.Web.Client.Screens.Rendering;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// Tests for animated art (frames, ranges, frame ANSI) and the <see cref="Slot"/> component that plays it in a
/// screen.
/// </summary>
public sealed class SlotTests
{
    /// <summary>The SysOp cam compiles to 16 frames at 8 a second, with idle and busy ranges.</summary>
    [Fact]
    public void CompilesTheCam()
    {
        var cam = SysopCamArt.Instance;
        Assert.True(cam.IsAnimated);
        Assert.Equal((24, 12, 16), (cam.Columns, cam.Rows, cam.Frames.Count));
        Assert.All(cam.Frames, f => Assert.Equal(125, f.DelayMs));
        Assert.Equal(new FrameRange(0, 7), cam.Range(SysopCamArt.Ranges.Idle));
        Assert.Equal(new FrameRange(8, 15), cam.Range(SysopCamArt.Ranges.Busy));
        Assert.Equal(new FrameRange(0, 15), cam.Range(null));
        Assert.Contains("'nope'", Assert.Throws<ArgumentException>(() => cam.Range("nope")).Message, StringComparison.Ordinal);
        Assert.Contains("SYSOP CAM", cam.Text, StringComparison.Ordinal);
        Assert.Empty(cam.Fields);

        Assert.False(MainMenuArt.Instance.IsAnimated);
        Assert.Single(MainMenuArt.Instance.Frames);
        Assert.Empty(MainMenuArt.Instance.FrameRanges);
        Assert.Contains("none", Assert.Throws<ArgumentException>(() => MainMenuArt.Instance.Range("idle")).Message, StringComparison.Ordinal);
    }

    /// <summary>Frames draw as box-relative ANSI, once each, and blink between frames.</summary>
    [Fact]
    public void DrawsFrameAnsi()
    {
        var cam = SysopCamArt.Instance;
        var first = cam.FrameAnsi(0);
        Assert.StartsWith(Ansi.MoveTo(1, 1), first, StringComparison.Ordinal);
        Assert.EndsWith(Ansi.Reset, first, StringComparison.Ordinal);
        Assert.Same(first, cam.FrameAnsi(0));
        Assert.NotEqual(first, cam.FrameAnsi(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => cam.FrameAnsi(16));
        Assert.Throws<ArgumentOutOfRangeException>(() => cam.FrameAnsi(-1));

        var frame = new ArtFrame("ab       c  ", [1, 2, 2, 2, 7, 7, 7, 7, 3, 3, 3, 3], [0, 5, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0], 40);
        Assert.Equal(
            Ansi.MoveTo(1, 1) + Ansi.Fg(1) + Ansi.Bg(0) + "a" + Ansi.Fg(2) + Ansi.Bg(5) + "b " +
            Ansi.MoveTo(3, 1) + Ansi.Fg(3) + Ansi.Bg(0) + " c" + Ansi.Reset,
            frame.ToAnsi(4));
        Assert.Equal(40, frame.DelayMs);
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.ToAnsi(0));
        Assert.Throws<ArgumentException>(() => new ArtFrame("ab", [1], [0, 0], 1));
        Assert.Throws<ArgumentException>(() => new ArtFrame("ab", [1, 1], [0], 1));
        Assert.Throws<ArgumentNullException>(() => new ArtFrame(null!, [], [], 1));
        Assert.Throws<ArgumentNullException>(() => new ArtFrame(string.Empty, null!, [], 1));
        Assert.Throws<ArgumentNullException>(() => new ArtFrame(string.Empty, [], null!, 1));
        Assert.Throws<ArgumentException>(() => new TinyArt(new ArtAnimation([new ArtFrame("xy", [7, 7], [0, 0], 1)], new Dictionary<string, FrameRange>())));
        Assert.True(new TinyArt(new ArtAnimation([new ArtFrame("x", [7], [0], 1), new ArtFrame("y", [7], [0], 1)], new Dictionary<string, FrameRange>())).IsAnimated);
    }

    /// <summary>A slot must sit inside a screen, in an animation slot the art fits, with a range the art has.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RefusesMisuse()
    {
        await using var p = new PageHarness();
        Assert.Throws<InvalidOperationException>(() => p.Bunit.Render<Slot>(ps => ps.Add(x => x.Art, SysopCamArt.Instance).Add(x => x.At, BulletinCamArt.Cam)));
        Assert.Throws<InvalidOperationException>(() => Screen(p, BulletinCamArt.Title));
        Assert.Throws<InvalidOperationException>(() => Screen(p, BulletinCamArt.Cam, art: MainMenuArt.Instance));
        Assert.Throws<ArgumentException>(() => Screen(p, BulletinCamArt.Cam, range: "nope"));
    }

    /// <summary>
    /// A slot starts once its screen is drawn, restarts when its parameters change while showing (not when they
    /// stay the same), and stops when it goes away; one outside the art never plays.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task StartsRestartsAndStops()
    {
        await using var p = new PageHarness(path: "/bulletins/2");
        var cut = Screen(p, BulletinCamArt.Cam);
        Assert.Empty(p.H.Terminal.SlotsStarted);
        p.H.Terminal.OnRead = k =>
        {
            switch (k)
            {
                case "s":
                    cut.Render(ps => ps.Add(x => x.Art, BulletinCamArt.Instance).AddChildContent<Slot>(c => c
                        .Add(x => x.Art, SysopCamArt.Instance).Add(x => x.At, BulletinCamArt.Cam)));
                    break;
                case "r":
                    cut.Render(ps => ps.Add(x => x.Art, BulletinCamArt.Instance).AddChildContent<Slot>(c => c
                        .Add(x => x.Art, SysopCamArt.Instance).Add(x => x.At, BulletinCamArt.Cam).Add(x => x.Range, "busy").Add(x => x.MaxDuration, TimeSpan.FromSeconds(5))));
                    break;
                case "g":
                    cut.Render(ps => ps.Add(x => x.Art, BulletinCamArt.Instance).AddChildContent<Field>(c => c
                        .Add(x => x.Slot, BulletinCamArt.Title).Add(x => x.Value, "gone")));
                    break;
            }
        };
        await p.RunAsync(ScreenId.Bulletins, "s", "r", "g", "x");

        Assert.Equal(["CAM", "CAM"], p.H.Terminal.SlotsStarted.Select(s => s.Name));
        Assert.Equal(16, p.H.Terminal.SlotsStarted[0].Animation.Frames.Count);
        Assert.Equal(8, p.H.Terminal.SlotsStarted[1].Animation.Frames.Count);
        Assert.Equal(TimeSpan.FromSeconds(5), p.H.Terminal.SlotsStarted[1].Animation.MaxDuration);
        Assert.Equal(["CAM"], p.H.Terminal.SlotsStopped);
        Assert.Empty(p.H.Terminal.Playing);

        var hidden = new PageHarness(path: "/bulletins/2");
        await using (hidden)
        {
            var other = hidden.Bunit.Render<BbsScreen>(ps => ps.Add(x => x.Art, BulletinArt.Instance).AddChildContent<Slot>(c => c
                .Add(x => x.Art, SysopCamArt.Instance).Add(x => x.At, BulletinCamArt.Cam)));
            other.WaitForState(() => hidden.H.Host.ScreenCount > 0);
            hidden.H.Terminal.OnRead = _ => other.Render(ps => ps.Add(x => x.Art, BulletinArt.Instance));
            await hidden.RunAsync(ScreenId.Bulletins, "g");
            Assert.Empty(hidden.H.Terminal.SlotsStarted);
            Assert.Empty(hidden.H.Terminal.SlotsStopped);
        }
    }

    private static IRenderedComponent<BbsScreen> Screen(PageHarness p, FieldSlot at, ScreenArt? art = null, string? range = null)
    {
        var cut = p.Bunit.Render<BbsScreen>(ps => ps
            .Add(x => x.Art, BulletinCamArt.Instance)
            .AddChildContent<Slot>(c => c.Add(x => x.Art, art ?? SysopCamArt.Instance).Add(x => x.At, at).Add(x => x.Range, range)));
        cut.WaitForState(() => p.H.Host.ScreenCount > 0);
        return cut;
    }

    /// <summary>A one-cell art, to check the frame size rule.</summary>
    /// <param name="animation">Its frames.</param>
    private sealed class TinyArt(ArtAnimation animation) : ScreenArt("tiny", 1, 1, "x", [7], [0], [], [], animation);
}
