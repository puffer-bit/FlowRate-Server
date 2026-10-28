using Shared.Models.Media;
using Shared.Models.Media.Transport;

namespace Server.Application.Orchestrator.SFUPool.Models;

public class SFUNodeModel
{
    public int Id { get; init; }
    public string ServerName { get; init; }
    
    public bool IsTunnelModeAvailable { get; init; } = false;

    public SFUEndpoint Endpoint { get; init; }
    public SFUCredentials Credentials { get; init; }
    
    public SFUNodeModel(
        string serverName, 
        SFUEndpoint endpoint,
        SFUCredentials credentials,
        bool isTunnelModeAvailable = false)
    {
        ServerName = serverName ?? throw new ArgumentNullException(nameof(serverName));
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        IsTunnelModeAvailable = isTunnelModeAvailable;
    }
}
