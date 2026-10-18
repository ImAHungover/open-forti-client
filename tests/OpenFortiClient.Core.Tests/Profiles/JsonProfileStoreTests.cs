using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.Core.Tests.Profiles;

public sealed class JsonProfileStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Fields_missing_from_the_file_keep_their_defaults()
    {
        var paths = new AppPaths(_temp.Path, _temp.Path);
        await File.WriteAllTextAsync(paths.ProfilesFile, """[{ "id": "11111111-1111-1111-1111-111111111111", "name": "Office" }]""");

        var profile = Assert.Single(await new JsonProfileStore(paths).GetAllAsync());

        Assert.Equal("Office", profile.Name);
        Assert.NotNull(profile.Gateways);
        Assert.NotNull(profile.TrustedCertDigests);
        Assert.Equal(AuthMode.PromptOnLogin, profile.AuthMode);
    }
}

public sealed class JsonProfileStoreAutoConnectTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Only_one_connection_connects_automatically()
    {
        var store = new JsonProfileStore(new AppPaths(_temp.Path, _temp.Path));
        var office = VpnProfileTests.Valid with { Id = Guid.NewGuid(), Name = "Office", AutoConnect = true };
        var home = VpnProfileTests.Valid with { Id = Guid.NewGuid(), Name = "Home" };
        await store.SaveAsync(office);
        await store.SaveAsync(home);

        await store.SaveAsync(home with { AutoConnect = true });

        var all = await store.GetAllAsync();
        Assert.False(all.Single(p => p.Name == "Office").AutoConnect);
        Assert.True(all.Single(p => p.Name == "Home").AutoConnect);
    }
}
