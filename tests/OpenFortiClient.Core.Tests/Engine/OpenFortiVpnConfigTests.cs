using OpenFortiClient.Core.Engine.OpenFortiVpn;

namespace OpenFortiClient.Core.Tests.Engine;

public class OpenFortiVpnConfigTests
{
    private static readonly OpenFortiVpnConfig Config = new()
    {
        Host = "vpn.example.com",
        Port = 10443,
        Username = "jane",
        Password = "s3cret",
        TrustedCertDigests = ["aa", "bb"],
        SetDns = false,
    };

    [Fact]
    public void Renders_openfortivpn_config_format()
    {
        Assert.Equal(
            """
            host = vpn.example.com
            port = 10443
            username = jane
            password = s3cret
            trusted-cert = aa
            trusted-cert = bb
            set-dns = 0
            set-routes = 1

            """.ReplaceLineEndings("\n"),
            Config.Render());
    }

    [Fact]
    public void Leaves_out_an_empty_password()
    {
        Assert.DoesNotContain("password", (Config with { Password = "" }).Render());
    }

    [Fact]
    public void Rejects_line_breaks_that_would_inject_options()
    {
        Assert.Throws<ArgumentException>(() => (Config with { Password = "x\nuser-cert = /etc/passwd" }).Render());
    }

    [Fact]
    public void ToString_does_not_reveal_the_password()
    {
        Assert.DoesNotContain("s3cret", Config.ToString());
    }
}
