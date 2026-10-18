using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.Core.Tests.Profiles;

public class GatewayAddressTests
{
    [Theory]
    [InlineData("vpn.example.com", "https://vpn.example.com/", 443)]
    [InlineData("vpn.example.com:10443", "https://vpn.example.com:10443/", 10443)]
    [InlineData("https://vpn.example.com:443", "https://vpn.example.com/", 443)]
    [InlineData("  https://vpn.example.com/remote/login  ", "https://vpn.example.com/", 443)]
    public void Normalises_gateway_input(string input, string expected, int port)
    {
        Assert.True(GatewayAddress.TryParse(input, out var gateway));
        Assert.Equal(expected, gateway.ToString());
        Assert.Equal(port, gateway.Port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://vpn.example.com")]
    [InlineData("https://")]
    public void Rejects_invalid_input(string? input)
    {
        Assert.False(GatewayAddress.TryParse(input, out _));
    }
}
