using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.Core.Tests.Profiles;

public sealed class ProfileFilesTests : IDisposable
{
    private const string Pem = "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n";
    private const string KeyPem = "-----BEGIN PRIVATE KEY-----\nMIIE\n-----END PRIVATE KEY-----\n";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_profile_without_a_client_certificate_has_nothing_to_check()
    {
        var report = ProfileFiles.Check(VpnProfileTests.Valid);

        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Valid_pem_files_with_a_private_key_pass()
    {
        var report = ProfileFiles.Check(WithFiles(Write("cert.pem", Pem), Write("key.pem", KeyPem, UnixFileMode.UserRead | UnixFileMode.UserWrite)));

        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Reports_a_missing_file()
    {
        var report = ProfileFiles.Check(WithFiles(Path.Combine(_temp.Path, "gone.pem"), Write("key.pem", KeyPem)));

        Assert.Contains(report.Errors, e => e.Contains("not found") && e.Contains("gone.pem"));
    }

    [Fact]
    public void Reports_a_file_that_is_not_pem()
    {
        var report = ProfileFiles.Check(WithFiles(Write("cert.der", "0\u0082binary"), Write("key.pem", KeyPem)));

        Assert.Contains(report.Errors, e => e.Contains("PEM") && e.Contains("cert.der"));
    }

    [Fact]
    public void Warns_when_other_users_can_read_the_key()
    {
        var key = Write("key.pem", KeyPem, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var report = ProfileFiles.Check(WithFiles(Write("cert.pem", Pem), key));

        Assert.Empty(report.Errors);
        Assert.Contains(report.Warnings, w => w.Contains("other users"));
    }

    private VpnProfile WithFiles(string cert, string key) =>
        VpnProfileTests.Valid with { ClientCertificatePath = cert, ClientKeyPath = key };

    private string Write(string name, string content, UnixFileMode? mode = null)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, content);
        if (mode is { } m && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, m);
        }

        return path;
    }
}
