using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.Core.Tests.Profiles;

public class VpnProfileTests
{
    internal static VpnProfile Valid => new()
    {
        Name = "Office",
        Gateways = [new Uri("https://vpn1.example.com"), new Uri("https://vpn2.example.com")],
        AuthMode = AuthMode.SaveLogin,
        Username = "jane",
    };

    [Fact]
    public void A_certificate_with_its_key_in_the_keychain_is_valid()
    {
        var profile = Valid with { ClientCertificatePath = "/certs/me.pem", ClientKeyInKeychain = true };

        Assert.Empty(profile.Validate());
    }

    [Fact]
    public void A_certificate_without_any_key_is_not()
    {
        var profile = Valid with { ClientCertificatePath = "/certs/me.pem" };

        Assert.Contains("Client certificate and key must be set together.", profile.Validate());
    }

    [Fact]
    public void A_complete_profile_is_valid()
    {
        Assert.Empty(Valid.Validate());
    }

    [Fact]
    public void Reports_missing_fields()
    {
        var problems = (Valid with { Name = " ", Gateways = [], Username = null }).Validate();

        Assert.Equal(3, problems.Count);
    }

    [Fact]
    public void Certificate_and_key_must_be_set_together()
    {
        Assert.Single((Valid with { ClientCertificatePath = "/tmp/cert.pem" }).Validate());
    }

    [Fact]
    public async Task Store_round_trips_profiles()
    {
        using var temp = new TempDirectory();
        var store = new JsonProfileStore(temp.AppPaths);
        var profile = Valid with { TrustedCertDigests = ["abc"] };

        await store.SaveAsync(profile);
        await store.SaveAsync(profile with { Description = "updated" });
        var loaded = await store.GetAllAsync();

        var single = Assert.Single(loaded);
        Assert.Equal("updated", single.Description);
        Assert.Equal(profile.Gateways, single.Gateways);
        Assert.Equal(profile.TrustedCertDigests, single.TrustedCertDigests);

        await store.DeleteAsync(profile.Id);
        Assert.Empty(await store.GetAllAsync());
    }
}
