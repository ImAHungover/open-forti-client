using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.Core.Tests.Engine;

public sealed class SystemVpnSettingsTests
{
    private static readonly SystemVpnSettings Valid = new("vpn.example.com", "user", SendAllTraffic: true, EchoIntervalSeconds: 10, EchoFailures: 3);

    [Fact]
    public void Valid_settings_have_no_problems() =>
        Assert.Empty(new SystemVpnSettingsChange(Valid, Password: "s3cret", SharedSecret: "shared").Validate());

    [Theory]
    [InlineData("vpn.example.com")]
    [InlineData("192.0.2.1")]
    [InlineData("2001:db8::1")]
    public void Accepts_host_names_and_addresses(string server) =>
        Assert.Empty(new SystemVpnSettingsChange(Valid with { Server = server }).Validate());

    [Theory]
    [InlineData("", "Server address is required.")]
    [InlineData("vpn.example.com; rm -rf /", "Server address must be a host name or an IP address.")]
    [InlineData("https://vpn.example.com", "Server address must be a host name or an IP address.")]
    public void Refuses_a_bad_server(string server, string message) =>
        Assert.Equal([message], new SystemVpnSettingsChange(Valid with { Server = server }).Validate());

    [Fact]
    public void Refuses_control_characters_in_the_account() =>
        Assert.Single(new SystemVpnSettingsChange(Valid with { Account = "user\nAuthPassword=x" }).Validate());

    [Theory]
    [InlineData(4, 3)]
    [InlineData(121, 3)]
    [InlineData(10, 0)]
    [InlineData(10, 31)]
    public void Refuses_keep_alive_outside_its_range(int interval, int failures) =>
        Assert.Single(new SystemVpnSettingsChange(Valid with { EchoIntervalSeconds = interval, EchoFailures = failures }).Validate());

    [Fact]
    public void Refuses_secrets_with_NUL_or_too_long()
    {
        var errors = new SystemVpnSettingsChange(Valid, Password: "a\0b", SharedSecret: new string('x', 257)).Validate();

        Assert.Equal(["Password is too long or contains a NUL character.", "Shared secret is too long or contains a NUL character."], errors);
    }

    [Fact]
    public void Secrets_left_empty_are_kept() =>
        Assert.Empty(new SystemVpnSettingsChange(Valid, Password: null, SharedSecret: null).Validate());

    [Fact]
    public void Says_how_long_a_dead_connection_goes_unnoticed() =>
        Assert.Equal(300, (Valid with { EchoIntervalSeconds = 20, EchoFailures = 15 }).DeadAfterSeconds);
}
