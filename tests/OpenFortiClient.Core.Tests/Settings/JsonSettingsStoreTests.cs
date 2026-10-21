using OpenFortiClient.Core.Settings;

namespace OpenFortiClient.Core.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Settings_missing_from_the_file_keep_their_defaults()
    {
        // An older file, a hand-edited one or a partial backup must not switch the certificate warning off.
        var paths = new AppPaths(_temp.Path, _temp.Path);
        await File.WriteAllTextAsync(paths.SettingsFile, """{ "gatewayTimeoutSeconds": 3 }""");

        var settings = await new JsonSettingsStore(paths).GetAsync();

        Assert.Equal(3, settings.GatewayTimeoutSeconds);
        Assert.True(settings.WarnInvalidServerCertificate);
        Assert.True(settings.SetDns);
        Assert.True(settings.SetRoutes);
        Assert.Equal(new AppSettings().OpenFortiVpnPath, settings.OpenFortiVpnPath);
        Assert.Empty(settings.KeepUpSystemVpns);
    }
}
