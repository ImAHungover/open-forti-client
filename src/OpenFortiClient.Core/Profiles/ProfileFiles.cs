namespace OpenFortiClient.Core.Profiles;

/// <summary>Problems with the files a profile points at; see <see cref="ProfileFiles.Check"/>.</summary>
public sealed record ProfileFileReport(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>
/// Checks the client certificate and key files of a profile. Unlike <see cref="VpnProfile.Validate"/>
/// this reads the disk, so it runs when a profile is saved and again before each connect (a file can
/// move or disappear in between).
/// </summary>
public static class ProfileFiles
{
    private const long MaxPemBytes = 1024 * 1024;
    private const UnixFileMode ReadableByOthers = UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    public static ProfileFileReport Check(VpnProfile profile)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        CheckPem("Client certificate", profile.ClientCertificatePath, errors);
        if (CheckPem("Client key", profile.ClientKeyPath, errors)
            && !OperatingSystem.IsWindows()
            && (File.GetUnixFileMode(profile.ClientKeyPath!) & ReadableByOthers) != 0)
        {
            warnings.Add($"The client key {profile.ClientKeyPath} can be read by other users. Restrict it with: chmod 600 \"{profile.ClientKeyPath}\"");
        }

        return new ProfileFileReport(errors, warnings);
    }

    /// <returns>true when the file exists and holds a PEM block.</returns>
    private static bool CheckPem(string what, string? path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!File.Exists(path))
        {
            errors.Add($"{what} file not found: {path}");
            return false;
        }

        try
        {
            if (new FileInfo(path).Length > MaxPemBytes || !File.ReadAllText(path).Contains("-----BEGIN ", StringComparison.Ordinal))
            {
                errors.Add($"{what} {path} is not a PEM file. openfortivpn needs PEM (text starting with \"-----BEGIN\").");
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{what} {path} cannot be read: {ex.Message}");
            return false;
        }

        return true;
    }
}
