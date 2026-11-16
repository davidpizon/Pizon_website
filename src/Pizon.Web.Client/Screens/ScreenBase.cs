using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// Base class for screens: holds the shared <see cref="ScreenContext"/> and small write helpers.
/// </summary>
public abstract class ScreenBase : IScreen
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScreenBase"/> class.
    /// </summary>
    /// <param name="context">The shared screen context.</param>
    protected ScreenBase(ScreenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
    }

    /// <inheritdoc />
    public abstract ScreenId Id { get; }

    /// <summary>Gets the shared screen context.</summary>
    protected ScreenContext Context { get; }

    /// <summary>Gets the terminal.</summary>
    protected ITerminal Term => Context.Terminal;

    /// <inheritdoc />
    public abstract Task<ScreenId> RunAsync(BbsSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Writes text containing pipe codes.
    /// </summary>
    /// <param name="pipeText">The text.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when painted.</returns>
    protected Task SayAsync(string pipeText, CancellationToken cancellationToken) =>
        Term.WriteAsync(Ui.P(pipeText), cancellationToken);
}
