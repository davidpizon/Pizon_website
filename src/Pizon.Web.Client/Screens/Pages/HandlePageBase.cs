using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Screens.Forms;
using Pizon.Web.Client.Screens.Hosting;

namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// The handle form, shared by <see cref="NewUserHandlePage"/> (a first sign-in picks a handle) and
/// <see cref="HandlePage"/> (a member changes theirs). The input starts with a suggestion: the Discord name
/// for a new user, the current handle for a member. Guests see ACCESS DENIED.
/// </summary>
public abstract class HandlePageBase : BbsPage
{
    private bool? newUser;

    /// <summary>Gets or sets the server API.</summary>
    [Inject]
    public required IBbsApi Api { get; set; }

    /// <summary>Gets the form.</summary>
    protected HandleModel Model { get; } = new();

    /// <summary>Gets or sets the status line (pipe codes).</summary>
    protected string? Status { get; set; }

    /// <summary>Gets or sets the prompt at the cursor once the form is done.</summary>
    protected string? Prompt { get; set; }

    /// <summary>Gets or sets a value indicating whether the form has been sent or abandoned.</summary>
    protected bool Done { get; set; }

    /// <summary>Gets a value indicating whether the page's address is the first-sign-in one.</summary>
    protected abstract bool ForNewUser { get; }

    /// <summary>Gets a value indicating whether the caller is choosing their first handle (settled when the screen draws).</summary>
    protected bool NewUser => newUser ?? IsNewUserNow;

    /// <summary>Gets a value indicating whether the caller may use the form.</summary>
    protected bool CanEdit => NewUser || Session.IsMember || Done;

    /// <summary>Gets the screen title.</summary>
    protected string Title => NewUser ? "NEW USER - CHOOSE YOUR HANDLE" : "CHANGE HANDLE";

    private bool IsNewUserNow => ForNewUser && Session.Me.Authenticated && Session.Me.Handle is null && !Session.ActingAsGuest;

    /// <inheritdoc />
    protected override void OnBeforeDraw()
    {
        if (newUser is null && !Done)
        {
            newUser = IsNewUserNow;
            Model.Handle = (newUser.Value ? Session.Me.SuggestedHandle : Session.IsMember ? Session.Me.Handle : null) ?? string.Empty;
        }
    }

    /// <summary>
    /// Registers the handle. A new user then finishes signing in (and goes where they were heading); a
    /// member sees the confirmation.
    /// </summary>
    /// <returns>A task that completes when done.</returns>
    protected async Task SubmitAsync()
    {
        var token = Host.PageToken;
        var result = await Api.SetHandleAsync(Model.Handle, token);
        if (!result.IsOk)
        {
            Status = Ui.FailureText(result);
            return;
        }

        Session.Me = await Api.GetMeAsync(token);
        Status = $"|10Handle registered: |15{PipeCodes.Escape(Model.Handle)}|10. Welcome to PIZON BBS.|07";
        Done = true;
        if (NewUser)
        {
            await ShowAsync();
            await FinishSignInAsync();
            return;
        }

        Prompt = Ui.AnyKey;
    }

    /// <summary>
    /// Escape: a new user carries on as GUEST for this call; a member goes back to the menu.
    /// </summary>
    /// <returns>A task that completes when done.</returns>
    protected async Task CancelAsync()
    {
        if (!NewUser)
        {
            Host.Navigate("/menu");
            return;
        }

        Session.ActingAsGuest = true;
        Status = "|08No handle, no problem: connecting as |14GUEST|08 for this call.|07";
        Done = true;
        await ShowAsync();
        await FinishSignInAsync();
    }

    /// <summary>
    /// Any key once the form is done (or for a guest who may not use it) goes back to the menu.
    /// </summary>
    /// <param name="key">The key.</param>
    protected void OnKey(string key)
    {
        if (Done || !CanEdit)
        {
            Host.Navigate("/menu");
        }
    }

    private Task FinishSignInAsync() => Host.LogOnAsync();
}
