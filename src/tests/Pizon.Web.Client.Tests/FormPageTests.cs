using Bunit;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Components;
using Pizon.Web.Client.Screens.Forms;
using Pizon.Web.Client.Screens.Pages;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Tests;

/// <summary>
/// Tests for <see cref="TextBox"/>, the editor behind every input slot.
/// </summary>
public sealed class TextBoxTests
{
    private static readonly FieldSlot Line = new("LINE", SlotKind.Input, 5, 10, 6, 1, 15, 0, TextAlign.Left);
    private static readonly FieldSlot Block = new("BLOCK", SlotKind.Input, 2, 1, 11, 3, 7, 0, TextAlign.Left);
    private static readonly string LineColors = Ansi.Fg(15) + Ansi.Bg(0);
    private static readonly string BlockColors = Ansi.Fg(7) + Ansi.Bg(0);

    /// <summary>Word wrap breaks after spaces (keeping them), cuts long words, and keeps blank lines.</summary>
    [Fact]
    public void WrapsWords()
    {
        Assert.Equal(["hello ", "world"], TextBox.Wrap("hello world", 6));
        Assert.Equal(["hello "], TextBox.Wrap("hello ", 6));
        Assert.Equal(["abc", "def", "gh"], TextBox.Wrap("abcdefgh", 3));
        Assert.Equal(["a", string.Empty, "b"], TextBox.Wrap("a\n\nb", 5));
        Assert.Equal([string.Empty], TextBox.Wrap(string.Empty, 4));
        Assert.Throws<ArgumentNullException>(() => TextBox.Wrap(null!, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextBox.Wrap("x", 0));
        Assert.Throws<ArgumentNullException>(() => new TextBox(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextBox(Line, -1));
    }

    /// <summary>
    /// A one-line box echoes each character with one positioned write, keeps a cell for the cursor, erases
    /// with Backspace, and refuses what doesn't fit (control characters, line breaks, a full line).
    /// </summary>
    [Fact]
    public void TypesOnOneLine()
    {
        var box = new TextBox(Line, 10);
        Assert.False(box.Multiline);
        Assert.Equal(5, box.LineWidth);
        Assert.Equal(Ansi.Reset + Ansi.MoveTo(5, 10) + LineColors + "a", box.Type("a"));
        foreach (var c in "bcde")
        {
            Assert.NotEmpty(box.Type(c.ToString()));
        }

        Assert.Equal("abcde", box.Text);
        Assert.Empty(box.Type("f"));
        Assert.Empty(box.Type("\u0007"));
        Assert.Empty(box.Type("xy"));
        Assert.Empty(box.NewLine());
        Assert.Equal(Ansi.Reset + Ansi.MoveTo(5, 14) + LineColors + " ", box.Backspace());
        Assert.Equal(Ansi.MoveTo(5, 14) + LineColors, box.Caret);
        box.Set("ab\ncdef");
        Assert.Equal("ab cd", box.Text);
        Assert.Throws<ArgumentNullException>(() => box.Set(null!));
        Assert.Throws<ArgumentNullException>(() => box.Type(null!));
        Assert.NotEmpty(box.Set(string.Empty));
        Assert.Empty(box.Backspace());
        Assert.True(box.EndsWithBlankLine);

        var short2 = new TextBox(Line, 2);
        short2.Type("a");
        short2.Type("b");
        Assert.Empty(short2.Type("c"));
    }

    /// <summary>
    /// A block wraps as you type and only rewrites what moved; Enter starts lines while they fit; text that
    /// doesn't fit is cut from the end; the whole slot can be drawn again.
    /// </summary>
    [Fact]
    public void WrapsABlock()
    {
        var box = new TextBox(Block, 25);
        Assert.True(box.Multiline);
        box.Set("aaaa bbbbb");
        Assert.Equal(["aaaa bbbbb"], box.Lines);
        var reflow = box.Type("b");
        Assert.Equal(
            Ansi.Reset + Ansi.MoveTo(2, 6) + BlockColors + "     " + Ansi.Reset + Ansi.MoveTo(3, 1) + BlockColors + "bbbbbb",
            reflow);
        Assert.Equal(Ansi.MoveTo(3, 7) + BlockColors, box.Caret);
        Assert.False(box.EndsWithBlankLine);
        Assert.Empty(box.NewLine());
        Assert.True(box.EndsWithBlankLine);
        Assert.Equal(Ansi.MoveTo(4, 1) + BlockColors, box.Caret);
        Assert.Empty(box.NewLine());
        Assert.Empty(box.TrimEnd());
        Assert.Equal("aaaa bbbbbb", box.Text);

        box.Set("hello world again and more");
        Assert.Equal("hello world again and ", box.Text);
        Assert.Equal(["hello ", "world ", "again and "], box.Lines);
        var all = box.Render();
        Assert.Contains(Ansi.MoveTo(4, 1) + BlockColors + "again and  ", all, StringComparison.Ordinal);
        Assert.Same(Block, box.Slot);
    }
}

/// <summary>
/// Tests for the form components' rules: where they may go and how keys move between inputs.
/// </summary>
public sealed class FormComponentTests
{
    /// <summary>Inputs and forms must sit inside a screen, inputs need input slots, and blocks need tall ones.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RefusesMisuse()
    {
        await using var p = new PageHarness();
        Assert.Throws<InvalidOperationException>(() => p.Bunit.Render<InputField>(ps => ps.Add(x => x.Slot, HandleArt.Handle)));
        Assert.Throws<InvalidOperationException>(() => p.Bunit.Render<BbsForm>(ps => ps.Add(x => x.Model, new HandleModel())));
        Assert.Throws<InvalidOperationException>(() => p.Bunit.Render<BbsScreen>(ps => ps
            .Add(x => x.Art, HandleArt.Instance)
            .AddChildContent<InputField>(c => c.Add(x => x.Slot, HandleArt.Title))));
        Assert.Throws<InvalidOperationException>(() => p.Bunit.Render<BbsScreen>(ps => ps
            .Add(x => x.Art, HandleArt.Instance)
            .AddChildContent<BlockEditor>(c => c.Add(x => x.Slot, HandleArt.Handle))));
    }

    /// <summary>
    /// An input outside a form takes typing (Enter does nothing more), and its text is in the screen-reader
    /// mirror under its label.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task TypesWithoutAForm()
    {
        await using var p = new PageHarness(path: "/handle");
        var text = string.Empty;
        var cut = p.Bunit.Render<BbsScreen>(ps => ps
            .Add(x => x.Art, HandleArt.Instance)
            .AddChildContent<InputField>(c => c
                .Add(x => x.Slot, HandleArt.Handle)
                .Add(x => x.Label, "Handle")
                .Add(x => x.Value, "Ne")
                .Add(x => x.ValueChanged, (string v) => text = v)));
        cut.WaitForState(() => p.H.Host.ScreenCount > 0);
        await p.RunAsync("o", DoorKeys.Enter, DoorKeys.Up, DoorKeys.Left);

        Assert.Equal("Neo", text);
        Assert.Equal([true, false], p.H.Terminal.LineModes);
        Assert.Contains("Handle: Ne", cut.Markup, StringComparison.Ordinal);
    }
}

/// <summary>
/// Tests for the terminal settings page.
/// </summary>
public sealed class SettingsPageTests
{
    /// <summary>Settings keys cycle baud and toggle sound and the RIP card.</summary>
    [Fact]
    public void AppliesSettingsKeys()
    {
        var s = new TerminalSettings();
        Assert.Equal(0, SettingsPage.Apply(s, "b")!.Baud);
        Assert.True(SettingsPage.Apply(s, "A")!.Sound);
        Assert.False(SettingsPage.Apply(s, "r")!.RipCard);
        Assert.Null(SettingsPage.Apply(s, "x"));
        Assert.Equal("|10ON", SettingsPage.OnOff(on: true));
        Assert.Equal("|09OFF", SettingsPage.OnOff(on: false));
    }

    /// <summary>Each key saves and applies one setting and sends just its line; other keys do nothing; Q leaves.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SavesEachChange()
    {
        await using var p = new PageHarness(path: "/settings");
        p.Show<SettingsPage>();
        await p.RunAsync("B", "A", "R", "x", "Q");

        Assert.Equal(new TerminalSettings(0, true, false), p.H.Session.Settings);
        Assert.Equal(3, p.H.Terminal.Applied.Count);
        Assert.Equal(p.H.Session.Settings, await TerminalSettings.LoadAsync(p.H.Terminal));
        var baud = Assert.Single(p.H.Terminal.Writes, w => PipeCodes.StripAnsi(w).Contains("INSTANT", StringComparison.Ordinal));
        Assert.StartsWith(Ansi.Reset + Ansi.MoveTo(SettingsArt.Baud.Row, SettingsArt.Baud.Column), baud, StringComparison.Ordinal);
        Assert.Equal([("/menu", false)], p.H.Navigator.Moves);
        p.AssertNoPlaceholders();
    }
}

/// <summary>
/// Tests for choosing and changing a handle.
/// </summary>
public sealed class HandlePageTests
{
    /// <summary>
    /// A new user starts from the suggestion, tries a taken handle, then a malformed one (the error clears as
    /// they type), then a good one, and is signed in. Typing is echoed one character at a time.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NewUserRetriesUntilAccepted()
    {
        var h = new Harness();
        h.Api.Me = new MeResponse(true, null, "Neo", 10, false, true, false);
        h.Session.Me = h.Api.Me;
        h.Api.HandleResults.Enqueue(new ApiResult(ApiStatus.Invalid, "taken"));
        await using var p = new PageHarness(h, "/handle/new");
        p.Show<NewUserHandlePage>();
        var left = await p.RunAsync(DoorKeys.Enter,
            DoorKeys.Backspace,
            DoorKeys.Backspace,
            DoorKeys.Backspace,
            "a",
            "b",
            DoorKeys.Enter,
            "c",
            DoorKeys.Enter);

        Assert.Equal("/rip", left);
        Assert.Equal(["handle:Neo", "handle:abc"], h.Api.Calls);
        Assert.Contains("NEW USER - CHOOSE YOUR HANDLE", p.Plain, StringComparison.Ordinal);
        Assert.Contains("REJECTED: TAKEN", p.Plain, StringComparison.Ordinal);
        Assert.Contains(HandleModel.BadShape, p.Plain, StringComparison.Ordinal);
        Assert.Contains("Handle registered: abc", p.Plain, StringComparison.Ordinal);
        var colors = Ansi.Fg(HandleArt.Handle.Foreground) + Ansi.Bg(0);
        Assert.Contains(Ansi.Reset + Ansi.MoveTo(8, 13) + colors + "c" + Ansi.MoveTo(8, 14) + colors, h.Terminal.Writes);
        Assert.Equal("abc", h.Session.Me.Handle);
        Assert.Equal(1, h.Presence.Connects);
        Assert.Contains(true, h.Terminal.LineModes);
        p.AssertNoPlaceholders();
    }

    /// <summary>A new user who presses Escape carries on as GUEST for the call.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NewUserEscapeStaysGuest()
    {
        var h = new Harness();
        h.Session.Me = new MeResponse(true, null, null, 10, false, true, false);
        h.Session.Settings = h.Session.Settings with { RipCard = false };
        await using var p = new PageHarness(h, "/handle/new");
        p.Show<NewUserHandlePage>();
        Assert.Equal("/menu", await p.RunAsync(DoorKeys.Escape));
        Assert.True(h.Session.ActingAsGuest);
        Assert.Equal("GUEST", h.Session.Handle);
        Assert.Contains("No handle, no problem", p.Plain, StringComparison.Ordinal);
        Assert.Empty(h.Api.Calls);
    }

    /// <summary>Guests can't change a handle; any key goes back to the menu.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GuestsCannotChangeHandle()
    {
        await using var p = new PageHarness(path: "/handle");
        p.Show<HandlePage>();
        await p.RunAsync("x");
        Assert.Contains("CHANGE HANDLE", p.Plain, StringComparison.Ordinal);
        Assert.Contains("ACCESS DENIED", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/menu", false)], p.H.Navigator.Moves);
        Assert.DoesNotContain(true, p.H.Terminal.LineModes);
    }

    /// <summary>A member changes their handle, sees why a save failed, and leaves with any key afterwards.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task MemberChangesHandle()
    {
        var h = new Harness().Member();
        h.Api.HandleResults.Enqueue(new ApiResult(ApiStatus.Failed));
        await using var p = new PageHarness(h, "/handle");
        var cut = p.Show<HandlePage>();
        await p.RunAsync(DoorKeys.Enter, DoorKeys.Backspace, DoorKeys.Backspace, DoorKeys.Backspace);
        h.Terminal.Press("Trinity").Keys(DoorKeys.Enter, "x");
        await p.RunAsync();

        Assert.Equal(["handle:Neo", "handle:Trinity"], h.Api.Calls);
        Assert.Contains("LINE NOISE", p.Plain, StringComparison.Ordinal);
        Assert.Contains("Handle registered: Trinity", p.Plain, StringComparison.Ordinal);
        Assert.Equal("Trinity", h.Session.Handle);
        Assert.Contains("Handle: Trinity", cut.Markup, StringComparison.Ordinal);
        Assert.Equal([("/menu", false)], h.Navigator.Moves);
    }

    /// <summary>A member who presses Escape goes back to the menu without saving.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task MemberEscapeLeaves()
    {
        var h = new Harness().Member();
        await using var p = new PageHarness(h, "/handle/new");
        p.Show<NewUserHandlePage>();
        await p.RunAsync(DoorKeys.Escape);
        Assert.Contains("CHANGE HANDLE", p.Plain, StringComparison.Ordinal);
        Assert.Empty(h.Api.Calls);
        Assert.Equal([("/menu", false)], h.Navigator.Moves);
    }
}

/// <summary>
/// Tests for paging the SysOp.
/// </summary>
public sealed class PageSysopPageTests
{
    /// <summary>Guests can't page; any key goes back to the menu.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GuestsCannotPage()
    {
        await using var p = new PageHarness(path: "/page");
        p.Show<PageSysopPage>();
        await p.RunAsync("x");
        Assert.Contains("ACCESS DENIED", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/menu", false)], p.H.Navigator.Moves);
        Assert.Empty(p.H.Api.Calls);
    }

    /// <summary>Escape or a blank message cancels without calling the server.</summary>
    /// <param name="key">The key that ends the form.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(DoorKeys.Escape)]
    [InlineData(DoorKeys.Enter)]
    public async Task CancelsAPage(string key)
    {
        await using var p = new PageHarness(new Harness().Member(), "/page");
        p.Show<PageSysopPage>();
        await p.RunAsync(key, "x");
        Assert.Contains("Page cancelled", p.Plain, StringComparison.Ordinal);
        Assert.Empty(p.H.Api.Calls);
        Assert.Equal([("/menu", false)], p.H.Navigator.Moves);
    }

    /// <summary>A member pages (three beeps), and a rate-limited page says so.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PagesTheSysop()
    {
        var h = new Harness().Member();
        await using var p = new PageHarness(h, "/page");
        p.Show<PageSysopPage>();
        h.Terminal.Press("Hi there").Keys(DoorKeys.Enter, "x");
        await p.RunAsync();
        Assert.Equal(3, h.Terminal.Beeps);
        Assert.Equal(["page:Hi there"], h.Api.Calls);
        Assert.Contains("SysOp has been alerted", p.Plain, StringComparison.Ordinal);
        Assert.Equal(62, PageSysopPage.MaxLength);

        var limited = new Harness().Member();
        limited.Api.WriteResult = new ApiResult(ApiStatus.RateLimited);
        await using var q = new PageHarness(limited, "/page");
        q.Show<PageSysopPage>();
        limited.Terminal.Press("again").Keys(DoorKeys.Enter);
        await q.RunAsync();
        Assert.Contains("SPEED RACER", q.Plain, StringComparison.Ordinal);
    }
}

/// <summary>
/// Tests for writing on the message board.
/// </summary>
public sealed class ComposePageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Guests are refused and nothing is posted; any key goes back to the board.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GuestsCannotPost()
    {
        await using var p = new PageHarness(path: "/board/new");
        p.Show<BoardNewPage>();
        await p.RunAsync("x");
        Assert.Contains("ACCESS DENIED", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/board", false)], p.H.Navigator.Moves);
        Assert.Empty(p.H.Api.Calls);
    }

    /// <summary>
    /// A member writes two lines (Enter breaks the line, a blank line sends it); the draft is cleared once it
    /// is queued.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PostsAMessage()
    {
        var h = new Harness().Member();
        await using var p = new PageHarness(h, "/board/new");
        p.Show<BoardNewPage>();
        h.Terminal.Press("Hello").Keys(DoorKeys.Enter).Press("World").Keys(DoorKeys.Enter, DoorKeys.Enter, "x");
        await p.RunAsync();

        Assert.Equal(["post::Hello\nWorld"], h.Api.Calls);
        Assert.Contains("NEW MESSAGE", p.Plain, StringComparison.Ordinal);
        Assert.Contains("MESSAGE QUEUED FOR SYSOP REVIEW", p.Plain, StringComparison.Ordinal);
        Assert.Null(h.Session.ComposeDraft);
        Assert.Equal([("/board", false)], h.Navigator.Moves);
        p.AssertNoPlaceholders();
    }

    /// <summary>Words wrap onto the next line as they are typed.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task WrapsAsYouType()
    {
        var h = new Harness().Member();
        await using var p = new PageHarness(h, "/board/new");
        var cut = p.Show<BoardNewPage>();
        h.Terminal.Press(string.Concat(Enumerable.Repeat("word ", 16)));
        await p.RunAsync();
        var editor = cut.FindComponent<BlockEditor>().Instance;
        Assert.Equal(2, editor.Box.Lines.Count);
        Assert.Equal(editor.Box.Text, h.Session.ComposeDraft);
    }

    /// <summary>Escape leaves the draft for the tab; the next visit starts from it. A blank message is abandoned.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task KeepsTheDraft()
    {
        var h = new Harness().Member();
        await using (var p = new PageHarness(h, "/board/new"))
        {
            p.Show<BoardNewPage>();
            h.Terminal.Press("Draft").Keys(DoorKeys.Escape);
            await p.RunAsync();
        }

        Assert.Equal("Draft", h.Session.ComposeDraft);
        Assert.Equal([("/board", false)], h.Navigator.Moves);

        await using var again = new PageHarness(h, "/board/new");
        var cut = again.Show<BoardNewPage>();
        Assert.Equal("Draft", cut.FindComponent<BlockEditor>().Instance.Box.Text);
        h.Terminal.Keys(DoorKeys.Backspace, DoorKeys.Backspace, DoorKeys.Backspace, DoorKeys.Backspace, DoorKeys.Backspace, DoorKeys.Enter);
        await again.RunAsync();
        Assert.Contains("Message abandoned", again.Plain, StringComparison.Ordinal);
        Assert.Null(h.Session.ComposeDraft);
        Assert.Empty(h.Api.Calls);
    }

    /// <summary>
    /// A reply needs a number (the error puts the cursor back in it), must answer a message on the wall, and
    /// then posts; the arrow keys move between the two inputs.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RepliesToAMessage()
    {
        var h = new Harness().Member();
        h.Api.Messages = new(ApiStatus.Ok, [new MessageDto(1, null, "Trinity", "First!", T0)]);
        await using var p = new PageHarness(h, "/board/reply");
        p.Show<BoardReplyPage>();
        h.Terminal
            .Press("x").Keys(DoorKeys.Down, DoorKeys.Enter)
            .Keys(DoorKeys.Backspace).Press("99").Keys(DoorKeys.Enter).Press("Reply").Keys(DoorKeys.Enter, DoorKeys.Enter)
            .Keys(DoorKeys.Up, DoorKeys.Backspace, DoorKeys.Backspace).Press("1").Keys(DoorKeys.Enter, DoorKeys.Enter, DoorKeys.Enter, "x");
        await p.RunAsync();

        Assert.Contains("REPLY TO A MESSAGE", p.Plain, StringComparison.Ordinal);
        Assert.Contains(ComposeModel.NoNumber, p.Plain, StringComparison.Ordinal);
        Assert.Contains("No such message on this wall", p.Plain, StringComparison.Ordinal);
        Assert.Equal(["post:1:Reply"], h.Api.Calls);
        Assert.Contains("MESSAGE QUEUED", p.Plain, StringComparison.Ordinal);
    }

    /// <summary>Failures to read the wall or to post are shown, and the message stays to try again.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ShowsFailures()
    {
        var h = new Harness().Member();
        h.Api.Messages = new(ApiStatus.Failed);
        await using var p = new PageHarness(h, "/board/reply");
        p.Show<BoardReplyPage>();
        h.Terminal.Press("1").Keys(DoorKeys.Enter).Press("Hi").Keys(DoorKeys.Enter, DoorKeys.Enter);
        await p.RunAsync();
        Assert.Contains("LINE NOISE", p.Plain, StringComparison.Ordinal);

        var limited = new Harness().Member();
        limited.Api.WriteResult = new ApiResult(ApiStatus.RateLimited);
        await using var q = new PageHarness(limited, "/board/new");
        q.Show<BoardNewPage>();
        limited.Terminal.Press("Hi").Keys(DoorKeys.Enter, DoorKeys.Enter);
        await q.RunAsync();
        Assert.Contains("SPEED RACER", q.Plain, StringComparison.Ordinal);
        Assert.Equal("Hi", limited.Session.ComposeDraft);
    }
}

/// <summary>
/// Tests for the SysOp console pages.
/// </summary>
public sealed class SysopPageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly PendingMessageDto[] Queue =
    [
        new(1, null, "Neo", 10, "approve me", T0),
        new(2, 1, "Spammer", 20, "buy now", T0),
        new(3, null, "Skip", 30, "later", T0),
    ];

    /// <summary>The console's links are only there for the SysOp, who follows them by number.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ConsoleLinksAreForTheSysop()
    {
        await using var member = new PageHarness(new Harness().Member(), "/sysop");
        var hidden = member.Show<SysopPage>();
        Assert.DoesNotContain("Moderation queue", hidden.Markup, StringComparison.Ordinal);

        await using var p = new PageHarness(new Harness().Member(sysop: true), "/sysop");
        var cut = p.Show<SysopPage>();
        Assert.Contains("/sysop/queue", cut.Markup, StringComparison.Ordinal);
        await p.RunAsync("3");
        Assert.Contains("SYSOP CONSOLE", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/sysop/delete", false)], p.H.Navigator.Moves);
    }

    /// <summary>The SysOp approves one message, bans the next one's author, skips the last, and is told the queue is clear.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Moderates()
    {
        var h = new Harness().Member(sysop: true);
        h.Api.Queue = new(ApiStatus.Ok, Queue);
        await using var p = new PageHarness(h, "/sysop/queue");
        p.Show<SysopQueuePage>();
        await p.RunAsync("z", "A", "B", "N", "x");

        Assert.Equal(["moderate:1:True", "ban:20", "moderate:2:False"], h.Api.Calls);
        Assert.Contains("[1/3]", p.Plain, StringComparison.Ordinal);
        Assert.Contains("reply to #1", p.Plain, StringComparison.Ordinal);
        Assert.Contains("That's the lot", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/sysop", false)], h.Navigator.Moves);
    }

    /// <summary>Failures are shown, a failed ban doesn't reject, and Q leaves the queue.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ShowsModerationFailures()
    {
        var h = new Harness().Member(sysop: true);
        h.Api.Queue = new(ApiStatus.Ok, Queue);
        h.Api.WriteResult = new ApiResult(ApiStatus.Forbidden);
        await using var p = new PageHarness(h, "/sysop/queue");
        p.Show<SysopQueuePage>();
        await p.RunAsync("X", "B", "Q");

        Assert.Equal(["moderate:1:False", "ban:10"], h.Api.Calls);
        Assert.Contains("THE SYSOP HAS SPOKEN", p.Plain, StringComparison.Ordinal);
        Assert.Equal([("/sysop", false)], h.Navigator.Moves);
    }

    /// <summary>An empty queue and a failed one say so.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task EmptyAndFailedQueues()
    {
        var h = new Harness().Member(sysop: true);
        await using var p = new PageHarness(h, "/sysop/queue");
        p.Show<SysopQueuePage>();
        await p.RunAsync("x");
        Assert.Contains("Nothing waiting", p.Plain, StringComparison.Ordinal);

        var f = new Harness().Member(sysop: true);
        f.Api.Queue = new(ApiStatus.Failed);
        await using var q = new PageHarness(f, "/sysop/queue");
        q.Show<SysopQueuePage>();
        await q.RunAsync("x");
        Assert.Contains("LINE NOISE", q.Plain, StringComparison.Ordinal);
    }

    /// <summary>The inbox stars unread pages and marks them read when the SysOp leaves after seeing them all.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ReadsPages()
    {
        var h = new Harness().Member(sysop: true);
        h.Api.Pages = new(ApiStatus.Ok, [new SysopPageDto(5, "Neo", "hello", T0, false), new SysopPageDto(6, "Old", "seen", T0, true)]);
        await using var p = new PageHarness(h, "/sysop/pages");
        p.Show<SysopPagesPage>();
        await p.RunAsync("x");

        Assert.Contains("PAGES INBOX", p.Plain, StringComparison.Ordinal);
        Assert.Contains("hello", p.Plain, StringComparison.Ordinal);
        Assert.Equal(["read:5"], h.Api.Calls);
        Assert.Equal([("/sysop", false)], h.Navigator.Moves);
    }

    /// <summary>Empty and failed inboxes say so, and nothing is marked.</summary>
    [Fact]
    public void EmptyAndFailedInboxes()
    {
        Assert.Contains("No pages", PipeCodes.StripToPlain(Assert.Single(SysopPagesPage.Lines(new(ApiStatus.Ok, [])))), StringComparison.Ordinal);
        Assert.Contains("LINE NOISE", PipeCodes.StripToPlain(Assert.Single(SysopPagesPage.Lines(new(ApiStatus.Failed)))), StringComparison.Ordinal);
    }

    /// <summary>
    /// Deleting needs a number, then Y: N goes back to the number, Y deletes, and any key returns to the console.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DeletesAMessage()
    {
        var h = new Harness().Member(sysop: true);
        await using var p = new PageHarness(h, "/sysop/delete");
        p.Show<SysopDeletePage>();
        h.Terminal
            .Press("abc").Keys(DoorKeys.Enter, DoorKeys.Backspace, DoorKeys.Backspace, DoorKeys.Backspace)
            .Press("7").Keys(DoorKeys.Enter, "z", "N", DoorKeys.Enter, "Y", "x");
        await p.RunAsync();

        Assert.Contains(DeleteMessageModel.NoNumber, p.Plain, StringComparison.Ordinal);
        Assert.Contains("Delete #7 and all its replies?", p.Plain, StringComparison.Ordinal);
        Assert.Contains("It never happened", p.Plain, StringComparison.Ordinal);
        Assert.Equal(["delete:7"], h.Api.Calls);
        Assert.Equal([("/sysop", false)], h.Navigator.Moves);
    }

    /// <summary>A failed delete says why; Escape leaves the form.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DeleteFailsOrLeaves()
    {
        var h = new Harness().Member(sysop: true);
        h.Api.WriteResult = new ApiResult(ApiStatus.Forbidden);
        await using var p = new PageHarness(h, "/sysop/delete");
        p.Show<SysopDeletePage>();
        h.Terminal.Press("7").Keys(DoorKeys.Enter, "Y");
        await p.RunAsync();
        Assert.Contains("THE SYSOP HAS SPOKEN", p.Plain, StringComparison.Ordinal);

        await using var q = new PageHarness(new Harness().Member(sysop: true), "/sysop/delete");
        q.Show<SysopDeletePage>();
        await q.RunAsync(DoorKeys.Escape);
        Assert.Equal([("/sysop", false)], q.H.Navigator.Moves);
    }
}
