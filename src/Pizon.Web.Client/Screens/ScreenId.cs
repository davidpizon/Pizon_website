namespace Pizon.Web.Client.Screens;

/// <summary>
/// Every screen of the BBS. Each screen's run returns the id of the next screen.
/// </summary>
public enum ScreenId
{
    /// <summary>The "press C to connect" screen.</summary>
    Connect,

    /// <summary>The dial-up handshake (ATDT, CONNECT 14400).</summary>
    Handshake,

    /// <summary>The graffiti banner and status header.</summary>
    Welcome,

    /// <summary>Discord sign-in or GUEST.</summary>
    Login,

    /// <summary>NEW USER - CHOOSE YOUR HANDLE.</summary>
    NewUserHandle,

    /// <summary>The RIPscrip welcome card.</summary>
    RipCard,

    /// <summary>The main menu.</summary>
    MainMenu,

    /// <summary>[1] System Bulletins.</summary>
    Bulletins,

    /// <summary>[2] Message Boards.</summary>
    MessageBoard,

    /// <summary>[N] on the message board: write a new message.</summary>
    BoardNew,

    /// <summary>[R] on the message board: reply to a message.</summary>
    BoardReply,

    /// <summary>[3] File Areas.</summary>
    FileAreas,

    /// <summary>[4] Door Games.</summary>
    Doors,

    /// <summary>[5] User List and Hall of Fame.</summary>
    UserList,

    /// <summary>[P] Page the SysOp.</summary>
    PageSysop,

    /// <summary>[H] Change handle.</summary>
    ChangeHandle,

    /// <summary>[S] Terminal settings.</summary>
    Settings,

    /// <summary>[G] Goodbye (logoff, NO CARRIER).</summary>
    Goodbye,

    /// <summary>[L] Sign out of Discord.</summary>
    Logout,

    /// <summary>[D] Sign in with Discord (leaves the page).</summary>
    SignIn,

    /// <summary>[*] The hidden SysOp console.</summary>
    SysopConsole,

    /// <summary>"File not found on this drive": a route the caller may not open (keeps it secret that it exists).</summary>
    NotFound,

    /// <summary>Stop the BBS loop (used when the page navigates away).</summary>
    Exit,
}
