using System.Net;

namespace Server.Application.Abstractions.Gateway;

/// <summary>Tells beforehand whether a coordinator could listen on an address and a port of this machine.</summary>
public interface IListenEndpointProbe
{
    /// <summary>The address is one of this machine, or one that listens on all of them.</summary>
    bool IsLocalAddress(IPAddress address);

    /// <summary>Nothing listens on the port of the address now, so it can be taken.</summary>
    bool IsPortFree(IPAddress address, int port);
}
