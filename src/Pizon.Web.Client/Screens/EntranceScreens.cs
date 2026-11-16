using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
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
        while (true)
        {
            await Term.ClearAsync(cancellationToken);
            await SayAsync(
                "\r\n\r\n|08  PIZON TERMINAL v1.0 · 80x25 · ANSI/256 · RIPscrip 1.54\r\n" +
                "|08  (c) 2026 PIZON.COM · All rights reserved, most wrongs regretted.\r\n\r\n" +
                "|07  Modem ready. Phonebook entry: |15PIZON BBS|07 · 555-0199\r\n\r\n",
                cancellationToken);
            await Term.WriteAsync(
                "    " + Ui.Item("C", Ui.P("|15CONNECT|07")) + "      " +
                Ui.Item("S", Ui.P($"Sound: |11{(session.Settings.Sound ? "ON " : "OFF")}|07")) + "\r\n\r\n",
                cancellationToken);
            await SayAsync("|08  Press |14C|08 or |14Enter|08 to dial. Tip: try it with sound on.|07", cancellationToken);

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
            }
        }
    }
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
    /// <summary>The banner art URL.</summary>
    public const string BannerUrl = "content/pizon.ans";

    /// <inheritdoc />
    public override ScreenId Id => ScreenId.Welcome;

    /// <inheritdoc />
    public override async Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var board = await Context.Content.GetBulletinsAsync(cancellationToken);
        var art = await Term.LoadArtAsync(BannerUrl, cancellationToken) ?? Ui.P("\r\n\r\n|#201   P I Z O N\r\n");
        var screen = Ui.Cls + art + Ui.P("|07\r\n") +
            Ui.P($"|#045{TextWrap.Center("B · B · S", 80)}\r\n") +
            Ui.P($"|08{TextWrap.Center(board.Tagline, 80)}|07\r\n\r\n") +
            Ui.StatusHeader(session, Context.Presence, board.Sysop, board.Mood, board.Location, Context.Time.GetLocalNow());
        await Term.TransitionAsync(Transition.Spray, screen, cancellationToken);
        Term.DiscardPendingKeys();
        return ScreenId.Login;
    }
}
