using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Server.Application.Abstractions.Gateway;

namespace Server.Infrastructure.Coordinator.Connection.Gateway;

public sealed class ListenEndpointProbe : IListenEndpointProbe
{
    public bool IsLocalAddress(IPAddress address)
    {
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || IPAddress.IsLoopback(address))
            return true;

        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Any(unicast => unicast.Address.Equals(address));
    }

    public bool IsPortFree(IPAddress address, int port)
    {
        // Taken and let go at once; the gateway binds it for real only after the settings were saved
        var listener = new TcpListener(address, port) { ExclusiveAddressUse = true };
        try
        {
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener.Stop();
        }
    }
}
