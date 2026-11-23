using System.Globalization;
using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Services;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// The login screen (<c>/login</c>): the graffiti PIZON banner, sprayed on, with the status header and
/// tagline, and below it the way in: sign in with Discord, or connect as GUEST. It asks the server who is
/// calling first, so a caller who is already signed in (or just back from Discord) sees WELCOME BACK and goes
/// straight on, and one who never chose a handle picks one first. After a failed sign-in it shows why.
/// </summary>
public sealed partial class LoginPage
{
    private BulletinBoard? board;
    private string? error;
    private bool signedIn;

    /// <summary>Gets or sets the server API.</summary>
    [Inject]
    public required IBbsApi Api { get; set; }

    /// <summary>Gets or sets the content store.</summary>
    [Inject]
    public required ContentStore Content { get; set; }

    /// <summary>Gets or sets presence (the node numbers).</summary>
    [Inject]
    public required IPresence Presence { get; set; }

    private string Prompt => signedIn
        ? $"|10WELCOME BACK, |15{PipeCodes.Escape(Session.Handle)}|10."
        : "How do you wish to connect?";

    /// <summary>
    /// Formats a date the way the status header shows it.
    /// </summary>
    /// <param name="now">The local time.</param>
    /// <returns>For example "MON NOV 23 2026".</returns>
    public static string Date(DateTimeOffset now) => now.ToString("ddd MMM dd yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();

    /// <summary>
    /// Lists the ways in for this caller: Discord when the server offers it, the Development-only sign-in
    /// when it is on, and GUEST.
    /// </summary>
    /// <param name="me">Who the server says is calling.</param>
    /// <param name="signedIn">Whether the caller is already signed in (no choices then).</param>
    /// <returns>Pipe-code text.</returns>
    public static string Options(MeResponse me, bool signedIn)
    {
        ArgumentNullException.ThrowIfNull(me);
        if (signedIn)
        {
            return string.Empty;
        }

        var options = string.Empty;
        if (me.DiscordEnabled)
        {
            options += Ui.ItemPipe("D", "Sign in with Discord") + "    ";
        }

        if (me.DevLoginEnabled)
        {
            options += Ui.ItemPipe("V", "|08Dev sign-in") + "    ";
        }

        return options + Ui.ItemPipe(DoorKeys.Enter, "Connect as GUEST");
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        error = Session.LoginError;
        Session.LoginError = null;
        Session.Me = await Api.GetMeAsync(CancellationToken.None);
        if (Session.Me.Authenticated && !Session.ActingAsGuest && Session.Me.Handle is null)
        {
            Host.Navigate("/handle/new", replace: true);
            return;
        }

        signedIn = Session.IsMember;
        board = await Content.GetBulletinsAsync(CancellationToken.None);
    }

    private async Task DrawnAsync()
    {
        Host.Terminal.DiscardPendingKeys();
        if (signedIn)
        {
            await Host.LogOnAsync();
        }
    }

    private async Task GuestAsync()
    {
        await Host.Terminal.WriteAsync(PipeCodes.ToAnsi("\r\n|07Connecting as |14GUEST|07. Security level 0. Wipe your feet."), Host.PageToken);
        await Host.LogOnAsync();
    }
}
