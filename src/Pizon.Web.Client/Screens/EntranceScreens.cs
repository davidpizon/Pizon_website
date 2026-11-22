using System.Globalization;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Screens.Art;
using Pizon.Web.Client.Screens.Rendering;
using Pizon.Web.Client.Services;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// The first screen: a ready terminal waiting for the caller to press Connect.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class ConnectScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.Connect;

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var view = new ScreenView(ConnectArt.Instance).Set(ConnectArt.Sound, SoundText(session.Settings));
        await Term.ClearAsync(cancellationToken);
        await Term.WriteAsync(view.Render(), cancellationToken);
        while (true)
        {
            var key = await Term.ReadKeyAsync(cancellationToken);
            if (DoorKeys.Is(key, 'C') || key == DoorKeys.Enter)
            {
                session.ConnectedAt = Context.Time.GetUtcNow();
                return ScreenId.Handshake;
            }

            if (DoorKeys.Is(key, 'S'))
            {
                session.Settings = session.Settings with { Sound = !session.Settings.Sound };
                await session.Settings.SaveAsync(Context.Storage);
                await Term.ApplySettingsAsync(session.Settings);
                await view.UpdateAsync(Term, ConnectArt.Sound, SoundText(session.Settings), cancellationToken);
            }
        }
    }

    private static string SoundText(TerminalSettings settings) => settings.Sound ? "ON" : "OFF";
}


/// <summary>
/// The dial-up handshake. Moving the mouse, tapping or pressing a key skips only this part.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class HandshakeScreen(ScreenContext context) : ScreenBase(context)
{
    /// <summary>The URL of the optional logo sting played after CONNECT.</summary>
    public const string StingUrl = "content/anim/sting.json";

    /// <summary>How long the silent handshake lasts.</summary>
    public static readonly TimeSpan SilentDuration = TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public override ScreenId Id => ScreenId.Handshake;

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        await Term.ClearAsync(cancellationToken);
        var audio = await Term.PlayHandshakeAsync();
        var total = audio > TimeSpan.Zero ? audio : SilentDuration;
        var steps = new (string Text, double Share)[]
        {
            ("|07ATZ\r\n|10OK\r\n", 0.10),
            ("|07ATDT 555-0199\r\n", 0.25),
            ("|08(move mouse or press any key to skip)\r\n", 0.0),
            ("|08RINGING...\r\n", 0.30),
            ("|08CARRIER DETECTED · NEGOTIATING V.32bis · LAPM · V.42bis\r\n", 0.35),
        };

        var skipped = false;
        foreach (var (text, share) in steps)
        {
            await SayAsync(text, cancellationToken);
            if (share > 0 && await Term.WaitOrSkipAsync(total * share, cancellationToken))
            {
                skipped = true;
                break;
            }
        }

        if (skipped)
        {
            await Term.StopAudioAsync();
        }

        await SayAsync("\r\n|15CONNECT 14400/ARQ/V32/LAPM/V42BIS|07\r\n", cancellationToken);
        await Term.DelayAsync(TimeSpan.FromMilliseconds(400), cancellationToken);
        Term.DiscardPendingKeys();
        await Term.PlayAnimationAsync(StingUrl, AnimationOptions.FullScreen with { MaxDuration = TimeSpan.FromSeconds(3) }, cancellationToken);
        return ScreenId.Welcome;
    }
}

/// <summary>
/// The graffiti PIZON banner, sprayed on, with the status header and tagline.
/// </summary>
/// <param name="context">The shared screen context.</param>
public sealed class WelcomeScreen(ScreenContext context) : ScreenBase(context)
{
    /// <inheritdoc />
    public override ScreenId Id => ScreenId.Welcome;

    /// <summary>
    /// Binds the welcome art: the tagline and the status lines.
    /// </summary>
    /// <param name="board">The bulletins (tagline, SysOp, mood, location).</param>
    /// <param name="session">The call.</param>
    /// <param name="presence">Presence, for the node numbers.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The bound view.</returns>
    public static ScreenView View(BulletinBoard board, BbsSession session, IPresence presence, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(presence);
        return new ScreenView(WelcomeArt.Instance)
            .Set(WelcomeArt.Tagline, board.Tagline)
            .Set(WelcomeArt.Sysop, board.Sysop)
            .Set(WelcomeArt.Node, presence.NodeNumber.ToString(CultureInfo.InvariantCulture))
            .Set(WelcomeArt.Nodes, presence.Online.ToString(CultureInfo.InvariantCulture))
            .Set(WelcomeArt.Date, now.ToString("ddd MMM dd yyyy", CultureInfo.InvariantCulture).ToUpperInvariant())
            .Set(WelcomeArt.Mood, board.Mood)
            .Set(WelcomeArt.Location, board.Location)
            .Set(WelcomeArt.Caller, PipeCodes.Escape(session.Handle))
            .Set(WelcomeArt.Level, session.Level.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var board = await Context.Content.GetBulletinsAsync(cancellationToken);
        var view = View(board, session, Context.Presence, Context.Time.GetLocalNow());
        await Term.TransitionAsync(Transition.Spray, view.Render(), cancellationToken);
        Term.DiscardPendingKeys();
        return ScreenId.Login;
    }
}
