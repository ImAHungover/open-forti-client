using OpenFortiClient.Core.Profiles;
using OpenFortiClient.Core.Settings;
using OpenFortiClient.Core.Tests.Profiles;

namespace OpenFortiClient.Core.Tests.Settings;

public sealed class ConfigurationBackupTests : IDisposable
{
    private readonly TempDirectory _from = new();
    private readonly TempDirectory _to = new();

    public void Dispose()
    {
        _from.Dispose();
        _to.Dispose();
    }

    [Fact]
    public async Task A_backup_restores_connections_and_settings()
    {
        var fromPaths = new AppPaths(_from.Path, _from.Path);
        var profile = VpnProfileTests.Valid with { AutoConnect = true, AlwaysUp = true, TrustedCertDigests = ["abc"] };
        await new JsonProfileStore(fromPaths).SaveAsync(profile);
        await new JsonSettingsStore(fromPaths).SaveAsync(new AppSettings { GatewayTimeoutSeconds = 42, ShowSystemNotifications = false });

        using var backup = new MemoryStream();
        await new ConfigurationBackupService(new JsonProfileStore(fromPaths), new JsonSettingsStore(fromPaths)).ExportAsync(backup);
        backup.Position = 0;
        var toPaths = new AppPaths(_to.Path, _to.Path);
        await new ConfigurationBackupService(new JsonProfileStore(toPaths), new JsonSettingsStore(toPaths)).ImportAsync(backup);

        var restored = Assert.Single(await new JsonProfileStore(toPaths).GetAllAsync());
        Assert.Equal(profile.Id, restored.Id);
        Assert.True(restored.AutoConnect);
        Assert.True(restored.AlwaysUp);
        Assert.Equal(["abc"], restored.TrustedCertDigests);
        var settings = await new JsonSettingsStore(toPaths).GetAsync();
        Assert.Equal(42, settings.GatewayTimeoutSeconds);
        Assert.False(settings.ShowSystemNotifications);
    }

    [Fact]
    public async Task A_backup_holds_no_passwords()
    {
        var paths = new AppPaths(_from.Path, _from.Path);
        await new JsonProfileStore(paths).SaveAsync(VpnProfileTests.Valid);

        using var backup = new MemoryStream();
        await new ConfigurationBackupService(new JsonProfileStore(paths), new JsonSettingsStore(paths)).ExportAsync(backup);

        Assert.DoesNotContain("password", System.Text.Encoding.UTF8.GetString(backup.ToArray()), StringComparison.OrdinalIgnoreCase);
    }
}
