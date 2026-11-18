using System.Net;
using Server.Infrastructure.Coordinator.LinkPreviews;
using Xunit;

namespace Server.Infrastructure.Tests.Coordinator.LinkPreviews;

public class PublicAddressesTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:192.168.1.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:c0a8:0101::1")]
    [InlineData("2001:0:4136:e378::1")]
    public void AddressesOfOwnNetworks_AreNotPublic(string address) =>
        Assert.False(PublicAddresses.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("2a00:1450:4010:c05::64")]
    public void InternetAddresses_ArePublic(string address) =>
        Assert.True(PublicAddresses.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData(80, true)]
    [InlineData(443, true)]
    [InlineData(22, false)]
    [InlineData(8080, false)]
    public void OnlyWebPorts_AreAllowed(int port, bool allowed) =>
        Assert.Equal(allowed, PublicAddresses.IsAllowedPort(port));
}
