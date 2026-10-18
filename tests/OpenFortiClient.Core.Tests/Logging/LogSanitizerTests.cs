using OpenFortiClient.Core.Logging;

namespace OpenFortiClient.Core.Tests.Logging;

public class LogSanitizerTests
{
    [Theory]
    [InlineData("DEBUG:  Configuration password = \"hunter2\"", "DEBUG:  Configuration password = ***")]
    [InlineData("DEBUG:  One-time password = \"123456\"", "DEBUG:  One-time password = ***")]
    [InlineData("password = hunter2", "password = ***")]
    [InlineData("openfortivpn -u jane -p hunter2 vpn:443", "openfortivpn -u jane -p *** vpn:443")]
    [InlineData("openfortivpn --password=hunter2", "openfortivpn --password=***")]
    [InlineData("Cookie: SVPNCOOKIE=abcdef; path=/", "Cookie: SVPNCOOKIE=***; path=/")]
    public void Masks_secrets(string input, string expected)
    {
        Assert.Equal(expected, LogSanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData("ERROR:  No password given")]
    [InlineData("VPN account password: ")]
    [InlineData("ERROR:  Could not authenticate to gateway. Please check the password, client certificate, etc.")]
    public void Leaves_harmless_lines_alone(string input)
    {
        Assert.Equal(input, LogSanitizer.Sanitize(input));
    }
}
