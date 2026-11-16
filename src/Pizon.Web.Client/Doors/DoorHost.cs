using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Pizon.Doors.Sdk;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Screens;
using Pizon.Web.Client.Services;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Doors;

/// <summary>
/// Runs doors: checks access, loads the door's code on demand, plays its intro and title card, runs it,
/// and puts the BBS back afterwards, even when the door crashes.
/// </summary>
public sealed class DoorHost
{
    /// <summary>The longest a door intro animation may play.</summary>
    public static readonly TimeSpan IntroLimit = TimeSpan.FromSeconds(6);

    private readonly ITerminal terminal;
    private readonly IBbsApi api;
    private readonly ContentStore content;
    private readonly IBrowserStorage storage;
    private readonly IDoorLoader loader;

    /// <summary>
    /// Initializes a new instance of the <see cref="DoorHost"/> class.
    /// </summary>
    /// <param name="terminal">The terminal.</param>
    /// <param name="api">The server API.</param>
    /// <param name="content">The content store.</param>
    /// <param name="storage">Browser storage.</param>
    /// <param name="loader">The door loader.</param>
    public DoorHost(ITerminal terminal, IBbsApi api, ContentStore content, IBrowserStorage storage, IDoorLoader loader)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(loader);
        this.terminal = terminal;
        this.api = api;
        this.content = content;
        this.storage = storage;
        this.loader = loader;
    }

    /// <summary>
    /// Builds a door's title card.
    /// </summary>
    /// <param name="entry">The door's manifest entry.</param>
    /// <returns>ANSI text.</returns>
    public static string TitleCard(DoorManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Ui.Cls + Ui.P(
            $"\r\n\r\n|#051{TextWrap.Center("* * *  D O O R  * * *", 80)}\r\n\r\n" +
            $"|15{TextWrap.Center(PipeCodes.Escape(entry.Title.ToUpperInvariant()), 80)}\r\n\r\n" +
            $"|07{TextWrap.Center(PipeCodes.Escape(entry.Description), 80)}\r\n\r\n" +
            $"|08{TextWrap.Center("Loading external program... press any key", 80)}|07\r\n");
    }

    /// <summary>
    /// Runs a door for this caller.
    /// </summary>
    /// <param name="entry">The door's manifest entry.</param>
    /// <param name="session">The call.</param>
    /// <param name="cancellationToken">Cancelled when the page goes away.</param>
    /// <returns>A task that completes when the caller is back at the BBS.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A door is third-party-style code; any failure inside it must leave the BBS running (DOOR CRASHED).")]
    public async Task RunAsync(DoorManifestEntry entry, BbsSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(session);
        if (entry.RequiresAuth && !session.IsMember)
        {
            await terminal.WriteAsync(Ui.AccessDenied(session), cancellationToken);
            await Ui.PressAnyKeyAsync(terminal, cancellationToken);
            return;
        }

        try
        {
            var door = await loader.LoadAsync(entry, cancellationToken);
            if (entry.Intro is { } intro && DoorContextAdapter.ResolveAsset(entry.AssetBase, intro) is { } introUrl)
            {
                terminal.DiscardPendingKeys();
                await terminal.PlayAnimationAsync(introUrl, AnimationOptions.FullScreen with { MaxDuration = IntroLimit }, cancellationToken);
            }

            await terminal.TransitionAsync(Transition.Dissolve, TitleCard(entry), cancellationToken);
            await terminal.ReadKeyAsync(TimeSpan.FromSeconds(4), cancellationToken);
            var today = await terminal.GetLocalDateAsync();
            var day = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var caller = session.Caller;
            var context = new DoorContextAdapter(
                terminal,
                content,
                entry,
                caller,
                today,
                new ApiDoorScores(api, entry.Id, caller, day),
                new NamespacedDoorStorage(storage, entry.Id));
            await terminal.ClearAsync(cancellationToken);
            await door.RunAsync(context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await terminal.WriteAsync(Ui.P("\r\n\r\n|09DOOR CRASHED - CARRIER RESTORED.|08 The SysOp has been blamed.|07\r\n"), cancellationToken);
            await terminal.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
        }
        finally
        {
            await terminal.SetLineModeAsync(false);
        }

        terminal.DiscardPendingKeys();
        await terminal.WriteAsync(Ui.P("\r\n|08Returning to PIZON BBS...|07"), cancellationToken);
        await terminal.DelayAsync(TimeSpan.FromMilliseconds(700), cancellationToken);
    }
}
