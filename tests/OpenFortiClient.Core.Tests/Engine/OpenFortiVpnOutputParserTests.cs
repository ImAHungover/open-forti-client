using OpenFortiClient.Core.Engine.OpenFortiVpn;

namespace OpenFortiClient.Core.Tests.Engine;

public class OpenFortiVpnOutputParserTests
{
    // Lines as printed by openfortivpn 1.24.1.
    [Theory]
    [InlineData("INFO:   Connected to gateway.", OutputEventKind.ConnectedToGateway)]
    [InlineData("INFO:   Authenticated.", OutputEventKind.Authenticated)]
    [InlineData("INFO:   Tunnel is up and running.", OutputEventKind.TunnelUp)]
    [InlineData("ERROR:  Gateway certificate validation failed, and the certificate digest is not in the local whitelist. If you trust it, rerun with:", OutputEventKind.CertificateUntrusted)]
    [InlineData("Two-factor authentication token: ", OutputEventKind.OtpPrompt)]
    [InlineData("Please enter one-time password: ", OutputEventKind.OtpPrompt)]
    [InlineData("VPN account password: ", OutputEventKind.PasswordPrompt)]
    [InlineData("ERROR:  Could not authenticate to gateway. Please check the password, client certificate, etc.", OutputEventKind.AuthenticationFailed)]
    [InlineData("ERROR:  Could not authenticate to the gateway. Please make sure tunnel mode is allowed by the gateway, check the realm, etc.", OutputEventKind.AuthenticationFailed)]
    [InlineData("ERROR:  connect: Operation timed out", OutputEventKind.GatewayUnreachable)]
    [InlineData("ERROR:  getaddrinfo: nodename nor servname provided, or not known", OutputEventKind.GatewayUnreachable)]
    [InlineData("INFO:   Closed connection to gateway.", OutputEventKind.TunnelClosed)]
    [InlineData("INFO:   Logged out.", OutputEventKind.TunnelClosed)]
    [InlineData("INFO:   Cancelling threads...", OutputEventKind.TunnelClosed)]
    [InlineData("ERROR:  Could not get VPN configuration (TLS error).", OutputEventKind.TunnelSetupFailed)]
    [InlineData("ERROR:  Could not start tunnel (Connection reset by peer).", OutputEventKind.TunnelSetupFailed)]
    [InlineData("sudo: a password is required", OutputEventKind.ElevationFailed)]
    [InlineData("ERROR:  This process was not spawned with root privileges, which are required.", OutputEventKind.ElevationFailed)]
    public void Recognises_known_messages(string line, OutputEventKind expected)
    {
        Assert.Equal(expected, OpenFortiVpnOutputParser.Parse(line)?.Kind);
    }

    [Theory]
    [InlineData("DEBUG:  openfortivpn 1.24.1")]
    [InlineData("INFO:   Interface ppp0 is UP.")]
    [InlineData("")]
    public void Ignores_other_lines(string line)
    {
        Assert.Null(OpenFortiVpnOutputParser.Parse(line));
    }

    [Fact]
    public void Extracts_the_certificate_digest()
    {
        const string digest = "4DAA8C5FE6C8B1A1F0E3C2D4B5A69788796A5B4C3D2E1F0A9B8C7D6E5F4A3B2C";

        var ev = OpenFortiVpnOutputParser.Parse($"    --trusted-cert {digest}");

        Assert.Equal(OutputEventKind.TrustedCertDigest, ev?.Kind);
        Assert.Equal(digest.ToLowerInvariant(), ev?.Value);
    }

    [Fact]
    public void Strips_the_log_level_from_the_value()
    {
        Assert.Equal("connect: Operation timed out", OpenFortiVpnOutputParser.Parse("ERROR:  connect: Operation timed out")?.Value);
    }
}
