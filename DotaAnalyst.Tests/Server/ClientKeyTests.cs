using System.Net;
using DotaAnalyst.Server.RateLimiting;

namespace DotaAnalyst.Tests.Server;

public class ClientKeyTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]                         // IPv4-mapped IPv6 = same client
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]     // counted by its /64 network
    [InlineData("2001:db8:1:2:1111:2222:3333:4444", "2001:db8:1:2::/64")]     // same /64 -> same key
    public void ClientKey_GroupsAddressesOfTheSameClient(string address, string expected)
    {
        Assert.Equal(expected, ApiRateLimiting.ClientKey(IPAddress.Parse(address)));
    }

    [Fact]
    public void ClientKey_HandlesMissingAddress()
    {
        Assert.Equal("unknown", ApiRateLimiting.ClientKey(null));
    }
}
