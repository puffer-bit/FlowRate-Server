using System.Net;
using System.Net.Sockets;

namespace Server.Infrastructure.Coordinator.LinkPreviews;

/// <summary>
/// Tells the addresses of the public internet from those of the server's own networks. The server may
/// sit on a router or next to other machines of a home or office network; a page fetched for a preview
/// must never come from there, nor from the server itself.
/// </summary>
public static class PublicAddresses
{
    /// <summary>The ports a preview is fetched from; anything else could be a service that is not a web site.</summary>
    public static bool IsAllowedPort(int port) => port is 80 or 443;

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicV6(address),
            _ => false
        };
    }

    private static bool IsPublicV4(byte[] b) => !(
        b[0] == 0                                          // 0.0.0.0/8, "this network"
        || b[0] == 10                                      // 10.0.0.0/8, private
        || (b[0] == 100 && (b[1] & 0xC0) == 64)            // 100.64.0.0/10, carrier-grade NAT
        || b[0] == 127                                     // 127.0.0.0/8, loopback
        || (b[0] == 169 && b[1] == 254)                    // 169.254.0.0/16, link-local
        || (b[0] == 172 && (b[1] & 0xF0) == 16)            // 172.16.0.0/12, private
        || (b[0] == 192 && b[1] == 0 && b[2] == 0)         // 192.0.0.0/24, protocol assignments
        || (b[0] == 192 && b[1] == 0 && b[2] == 2)         // 192.0.2.0/24, documentation
        || (b[0] == 192 && b[1] == 88 && b[2] == 99)       // 192.88.99.0/24, 6to4 relay
        || (b[0] == 192 && b[1] == 168)                    // 192.168.0.0/16, private
        || (b[0] == 198 && (b[1] & 0xFE) == 18)            // 198.18.0.0/15, benchmarking
        || (b[0] == 198 && b[1] == 51 && b[2] == 100)      // 198.51.100.0/24, documentation
        || (b[0] == 203 && b[1] == 0 && b[2] == 113)       // 203.0.113.0/24, documentation
        || b[0] >= 224);                                   // multicast, reserved and broadcast

    private static bool IsPublicV6(IPAddress address)
    {
        var b = address.GetAddressBytes();

        // Only global unicast, 2000::/3, is public at all
        if ((b[0] & 0xE0) != 0x20)
            return false;

        // 2001::/32 Teredo and 2001:db8::/32 documentation; 2002::/16 6to4 carries any IPv4 address
        if (b[0] == 0x20 && b[1] == 0x01 && ((b[2] == 0 && b[3] == 0) || (b[2] == 0x0D && b[3] == 0xB8)))
            return false;
        if (b[0] == 0x20 && b[1] == 0x02)
            return false;

        return !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal && !address.IsIPv6Multicast;
    }
}
