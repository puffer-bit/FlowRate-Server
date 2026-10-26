using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Server.Application.Orchestrator.SFUPool.Models;
using Server.Core.Models.Orchestrator;
using Server.Application.Abstractions.Sfu;
using Server.Application.Orchestrator.Settings;

namespace Server.Application.Orchestrator.SFUPool;

public class SfuNodeRegistry : ISfuNodeRegistry
{
    public event Func<Task>? NodesChanged;
    public event Action? NodeStatusChanged;

    private readonly ConcurrentDictionary<int, ISFUNode> _nodes = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly ILogger _logger;

    private readonly ISFUNodeFactory _nodeFactory;
    private readonly ISfuNodeStore _nodeStore;
    private volatile bool _isLoaded;

    public SfuNodeRegistry(ILoggerFactory loggerFactory, ISFUNodeFactory nodeFactory, ISfuNodeStore nodeStore)
    {
        _logger = loggerFactory.CreateLogger($"Orchestrator");

        _nodeFactory = nodeFactory;
        _nodeStore = nodeStore;

        // Nodes added or changed later (from the control panel) are picked up without a restart.
        // Changes made before the first load (seeding) are picked up by LoadAsync itself.
        _nodeStore.Changed += () => _isLoaded ? SyncOnChangeAsync() : Task.CompletedTask;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await SyncAsync(connectNewNodes: false, ct);
        _isLoaded = true;
    }

    public async Task ConnectAllAsync()
    {
        await Task.WhenAll(_nodes.Values
            .Where(node => !node.Gateway.IsConnected)
            .Select(ConnectNodeAsync));
    }

    private async Task ConnectNodeAsync(ISFUNode node)
    {
        try
        {
            await node.Connect();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connecting to SFU node {Name} failed.", node.Model.ServerName);
        }
    }

    private async Task SyncOnChangeAsync()
    {
        try
        {
            await SyncAsync(connectNewNodes: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Applying SFU node changes failed. The pool keeps its previous nodes.");
        }
    }

    public ISFUNode? GetNode(int nodeId) => _nodes.GetValueOrDefault(nodeId);

    public SfuNodeStatus? GetStatus(int nodeId) => GetNode(nodeId)?.Gateway.Status;

    private void RaiseNodeStatusChanged() => NodeStatusChanged?.Invoke();

    private async Task SyncAsync(bool connectNewNodes, CancellationToken ct = default)
    {
        if (await SyncNodesAsync(connectNewNodes, ct))
        {
            RaiseNodeStatusChanged();
            await RaiseNodesChangedAsync();
        }
    }

    /// <returns>Whether the set of nodes changed.</returns>
    private async Task<bool> SyncNodesAsync(bool connectNewNodes, CancellationToken ct)
    {
        await _syncLock.WaitAsync(ct);
        try
        {
            var settings = (await _nodeStore.GetAllAsync(ct))
                .Where(node => node.IsEnabled)
                .ToDictionary(node => node.Id);
            var isChanged = false;

            // Drop nodes that were removed, disabled or changed (changed ones are recreated below)
            foreach (var (id, node) in _nodes)
            {
                if (settings.TryGetValue(id, out var nodeSettings) && !IsChanged(node.Model, nodeSettings))
                    continue;

                _nodes.TryRemove(id, out _);
                node.Gateway.StatusChanged -= RaiseNodeStatusChanged;
                isChanged = true;
                try
                {
                    await node.Disconnect();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Disconnecting SFU node {Name} failed.", node.Model.ServerName);
                }
                _logger.LogInformation("SFU node {Name} removed from the pool.", node.Model.ServerName);
            }

            foreach (var nodeSettings in settings.Values.Where(s => !_nodes.ContainsKey(s.Id)))
            {
                var node = _nodeFactory.CreateSFUNode(ToNodeModel(nodeSettings));
                _nodes.TryAdd(nodeSettings.Id, node);
                node.Gateway.StatusChanged += RaiseNodeStatusChanged;
                isChanged = true;
                _logger.LogInformation("SFU node {Name} ({Address}) added to the pool.",
                    nodeSettings.Name, node.Model.Endpoint.Address);

                if (connectNewNodes)
                    _ = ConnectNodeAsync(node);
            }

            if (_nodes.IsEmpty)
                _logger.LogWarning("SFU node pool is empty. Media sessions will not work until a node is added.");

            return isChanged;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task RaiseNodesChangedAsync()
    {
        if (NodesChanged == null)
            return;

        // Every coordinator subscribes, so each handler is awaited on its own
        foreach (var handler in NodesChanged.GetInvocationList().Cast<Func<Task>>())
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handling a change of the SFU node pool failed.");
            }
        }
    }

    private static SFUNodeModel ToNodeModel(SfuNodeSettingsModel settings) =>
        new(settings.Name,
            new SFUEndpoint(settings.Host, (ushort)settings.Port) { IsSecured = settings.IsSecured },
            new SFUCredentials(settings.Username, settings.Key))
        {
            Id = settings.Id
        };

    private static bool IsChanged(SFUNodeModel current, SfuNodeSettingsModel settings) =>
        current.ServerName != settings.Name
        || current.Endpoint.Port != settings.Port
        || current.Endpoint.IsSecured != settings.IsSecured
        || !string.Equals(current.Endpoint.Host, settings.Host.Trim(), StringComparison.OrdinalIgnoreCase)
        || current.Credentials.Username != settings.Username.Trim()
        || current.Credentials.Key != settings.Key;
}
