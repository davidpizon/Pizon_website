namespace Pizon.Web.Client.Services;

/// <summary>
/// Callers-online presence: this caller's node number and who else is connected.
/// </summary>
public interface IPresence
{
    /// <summary>Raised when the online count or node list changes.</summary>
    event Action? Changed;

    /// <summary>Gets this caller's node number (1 until connected).</summary>
    int NodeNumber { get; }

    /// <summary>Gets the busy node numbers, ascending.</summary>
    IReadOnlyList<int> Nodes { get; }

    /// <summary>Gets the number of callers online (at least 1: this caller).</summary>
    int Online { get; }

    /// <summary>Connects to the presence hub (safe to call more than once).</summary>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>A task that completes when connected, or when the hub is unreachable.</returns>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Disconnects (logoff frees the node immediately).</summary>
    /// <returns>A task that completes when disconnected.</returns>
    Task DisconnectAsync();
}

/// <summary>
/// Presence protocol names shared by the hub and the client.
/// </summary>
public static class PresenceProtocol
{
    /// <summary>The hub path.</summary>
    public const string HubPath = "hubs/node";

    /// <summary>Server-to-client: the busy node list (sent to everyone when it changes).</summary>
    public const string NodesMessage = "Nodes";

    /// <summary>Server-to-client: this connection's node number.</summary>
    public const string AssignedMessage = "Assigned";
}
