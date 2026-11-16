using Microsoft.AspNetCore.SignalR;
using Pizon.Web.Client.Services;

namespace Pizon.Web.Hubs;

/// <summary>
/// Hands out node numbers: each connection gets the lowest free number. One server instance, so memory is enough.
/// </summary>
public sealed class NodeRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, int> nodes = [];

    /// <summary>
    /// Assigns a node to a connection (the same node if it already has one).
    /// </summary>
    /// <param name="connectionId">The connection id.</param>
    /// <returns>The node number.</returns>
    public int Assign(string connectionId)
    {
        lock (gate)
        {
            if (nodes.TryGetValue(connectionId, out var existing))
            {
                return existing;
            }

            var used = nodes.Values.ToHashSet();
            var node = 1;
            while (used.Contains(node))
            {
                node++;
            }

            nodes[connectionId] = node;
            return node;
        }
    }

    /// <summary>
    /// Frees a connection's node.
    /// </summary>
    /// <param name="connectionId">The connection id.</param>
    /// <returns><see langword="true"/> when a node was freed.</returns>
    public bool Release(string connectionId)
    {
        lock (gate)
        {
            return nodes.Remove(connectionId);
        }
    }

    /// <summary>
    /// Gets the busy node numbers, ascending.
    /// </summary>
    /// <returns>The nodes.</returns>
    public int[] Busy()
    {
        lock (gate)
        {
            return [.. nodes.Values.Order()];
        }
    }
}

/// <summary>
/// The presence hub at <c>/hubs/node</c>: tells each caller its node and everyone the busy nodes.
/// Counts include guests; no handles are shared.
/// </summary>
/// <param name="registry">The node registry.</param>
public sealed class NodeHub(NodeRegistry registry) : Hub
{
    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        var node = registry.Assign(Context.ConnectionId);
        await Clients.Caller.SendAsync(PresenceProtocol.AssignedMessage, node);
        await Clients.All.SendAsync(PresenceProtocol.NodesMessage, registry.Busy());
        await base.OnConnectedAsync();
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (registry.Release(Context.ConnectionId))
        {
            await Clients.All.SendAsync(PresenceProtocol.NodesMessage, registry.Busy());
        }

        await base.OnDisconnectedAsync(exception);
    }
}
