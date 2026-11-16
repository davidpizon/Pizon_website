using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace Pizon.Web.Client.Services;

/// <summary>
/// <see cref="IPresence"/> over the SignalR <c>NodeHub</c>.
/// </summary>
public sealed class HubPresence : IPresence, IAsyncDisposable
{
    private readonly NavigationManager navigation;
    private HubConnection? connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="HubPresence"/> class.
    /// </summary>
    /// <param name="navigation">Used to resolve the hub URL against the site root.</param>
    public HubPresence(NavigationManager navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        this.navigation = navigation;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public int NodeNumber { get; private set; } = 1;

    /// <inheritdoc />
    public IReadOnlyList<int> Nodes { get; private set; } = [1];

    /// <inheritdoc />
    public int Online => Math.Max(1, Nodes.Count);

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (connection is not null)
        {
            return;
        }

        connection = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri(PresenceProtocol.HubPath))
            .WithAutomaticReconnect()
            .Build();
        connection.On<int>(PresenceProtocol.AssignedMessage, Assign);
        connection.On<int[]>(PresenceProtocol.NodesMessage, Update);
        try
        {
            await connection.StartAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Presence is decorative: without it the board still works as "Node 1 of 1".
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        if (connection is null)
        {
            return;
        }

        var c = connection;
        connection = null;
        await c.DisposeAsync();
        Update([1]);
        NodeNumber = 1;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => connection?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// Applies a node assignment from the hub.
    /// </summary>
    /// <param name="node">This connection's node number.</param>
    public void Assign(int node)
    {
        NodeNumber = node;
        Changed?.Invoke();
    }

    /// <summary>
    /// Applies a node-list update from the hub.
    /// </summary>
    /// <param name="nodes">The busy nodes.</param>
    public void Update(int[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        Nodes = [.. nodes.Order()];
        Changed?.Invoke();
    }
}
