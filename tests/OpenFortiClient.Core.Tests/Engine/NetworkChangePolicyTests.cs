using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.Core.Tests.Engine;

public sealed class NetworkChangePolicyTests
{
    [Fact]
    public void The_network_coming_back_is_worth_a_retry()
    {
        Assert.True(NetworkChangePolicy.ShouldRetry(new NetworkChange(true, [], []), VpnStatus.Error));
    }

    [Fact]
    public void A_new_physical_interface_is_worth_a_retry()
    {
        Assert.True(NetworkChangePolicy.ShouldRetry(new NetworkChange(false, ["en7"], []), VpnStatus.Error));
    }

    [Fact]
    public void Another_vpn_coming_up_is_worth_a_retry()
    {
        // The company L2TP VPN (ppp0) is back, so the gateway is reachable again.
        Assert.True(NetworkChangePolicy.ShouldRetry(new NetworkChange(false, ["ppp0"], []), VpnStatus.Error));
    }

    [Theory]
    [InlineData("ppp0")]
    [InlineData("ppp1")]
    [InlineData("utun4")]
    public void A_tunnel_going_away_is_not(string name)
    {
        // Review issue 1: the dropped tunnel's own ppp interface used to skip the back-off.
        Assert.False(NetworkChangePolicy.ShouldRetry(new NetworkChange(false, [], [name]), VpnStatus.Error));
    }

    [Theory]
    [InlineData(VpnStatus.Connecting)]
    [InlineData(VpnStatus.Authenticating)]
    [InlineData(VpnStatus.Connected)]
    public void The_apps_own_tunnel_coming_up_is_not(VpnStatus status)
    {
        // While openfortivpn starts, the ppp interface that appears is its own.
        Assert.False(NetworkChangePolicy.ShouldRetry(new NetworkChange(false, ["ppp1"], []), status));
    }

    [Fact]
    public void A_physical_interface_going_away_is_not()
    {
        Assert.False(NetworkChangePolicy.ShouldRetry(new NetworkChange(false, [], ["en0"]), VpnStatus.Error));
    }
}
