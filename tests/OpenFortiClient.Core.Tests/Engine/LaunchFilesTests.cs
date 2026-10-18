using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.Core.Tests.Engine;

[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed class LaunchFilesTests : IDisposable
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Run => Path.Combine(_temp.Path, "run");

    [Fact]
    public void Writes_the_config_only_the_owner_can_read()
    {
        using var files = LaunchFiles.Write(new LaunchRequest("/x/openfortivpn", "host = vpn\npassword = s3cret\n", []), Run);

        Assert.Equal(OwnerReadWrite, File.GetUnixFileMode(files.ConfigPath));
        Assert.Equal(OwnerReadWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Run));
        Assert.Equal("host = vpn\npassword = s3cret\n", File.ReadAllText(files.ConfigPath));
        Assert.Null(files.KeyPath);
    }

    [Fact]
    public void Writes_the_client_key_next_to_it_and_points_the_config_at_it()
    {
        var request = new LaunchRequest("/x/openfortivpn", "host = vpn\n", []) { ClientKeyPem = "-----BEGIN PRIVATE KEY-----\n" };

        using var files = LaunchFiles.Write(request, Run);

        Assert.NotNull(files.KeyPath);
        Assert.Equal(OwnerReadWrite, File.GetUnixFileMode(files.KeyPath));
        Assert.Equal("-----BEGIN PRIVATE KEY-----\n", File.ReadAllText(files.KeyPath));
        Assert.Equal($"host = vpn\nuser-key = {files.KeyPath}\n", File.ReadAllText(files.ConfigPath));
    }

    [Fact]
    public void DeleteConfig_keeps_the_key_that_openfortivpn_still_needs()
    {
        using var files = LaunchFiles.Write(new LaunchRequest("/x/openfortivpn", "host = vpn\n", []) { ClientKeyPem = "key" }, Run);

        files.DeleteConfig();

        Assert.False(File.Exists(files.ConfigPath));
        Assert.True(File.Exists(files.KeyPath));
    }

    [Fact]
    public void Dispose_removes_both_files_and_can_run_twice()
    {
        var files = LaunchFiles.Write(new LaunchRequest("/x/openfortivpn", "host = vpn\n", []) { ClientKeyPem = "key" }, Run);

        files.Dispose();
        files.Dispose();

        Assert.Empty(Directory.EnumerateFiles(Run));
    }
}
