using Pizon.Doors.Sdk;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// The state of one call: who is connected, since when, and their terminal settings.
/// </summary>
public sealed class BbsSession
{
    /// <summary>Gets or sets the caller's identity from the server.</summary>
    public MeResponse Me { get; set; } = MeResponse.Guest;

    /// <summary>Gets or sets when the caller connected.</summary>
    public DateTimeOffset ConnectedAt { get; set; }

    /// <summary>Gets or sets the terminal settings.</summary>
    public TerminalSettings Settings { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether the page loaded straight back from Discord sign-in.</summary>
    public bool Resumed { get; set; }

    /// <summary>Gets or sets the sign-in error to show on the login screen, if any.</summary>
    public string? LoginError { get; set; }

    /// <summary>Gets or sets a value indicating whether the caller chose to stay a guest this call.</summary>
    public bool ActingAsGuest { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the caller has logged on this call: chose GUEST or signed in at
    /// the login screen, or opened a link while already signed in.
    /// </summary>
    public bool SignedOn { get; set; }

    /// <summary>Gets or sets the routed path to open once the caller has logged on (from a link or <c>returnUrl</c>).</summary>
    public string? ReturnTo { get; set; }

    /// <summary>Gets or sets a value indicating whether the call started from a link straight to a screen (no dial-up).</summary>
    public bool DeepLink { get; set; }

    /// <summary>Gets a value indicating whether the caller is signed in with a handle (and not acting as a guest).</summary>
    public bool IsMember => Me.Authenticated && Me.Handle is not null && !ActingAsGuest;

    /// <summary>Gets the handle shown on screen.</summary>
    public string Handle => IsMember ? Me.Handle! : "GUEST";

    /// <summary>Gets the security level shown on screen.</summary>
    public int Level => !IsMember ? SecurityLevels.Guest : Me.IsSysop ? SecurityLevels.Sysop : SecurityLevels.User;

    /// <summary>Gets a value indicating whether the caller is the SysOp.</summary>
    public bool IsSysop => IsMember && Me.IsSysop;

    /// <summary>Gets the caller as seen by doors.</summary>
    public DoorCaller Caller => new(Handle, IsMember, Level);
}
