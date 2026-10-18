using System.Text.Json;
using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.Core.Settings;

/// <summary>
/// The content of a backup file: profiles and settings, never secrets.
/// </summary>
public sealed record ConfigurationBackup
{
    // Properties use set, not init: the source-generated JSON reader fills init properties like
    // constructor arguments, so a property missing from the file got default(T) instead of the value
    // below (a missing WarnInvalidServerCertificate became false). Covered by the JsonSettingsStore
    // and VpnProfile tests.

    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<VpnProfile> Profiles { get; set; } = [];

    public AppSettings Settings { get; set; } = new();
}

/// <summary>Backup and restore from the Settings page.</summary>
public sealed class ConfigurationBackupService(IProfileStore profiles, ISettingsStore settings)
{
    public async Task ExportAsync(Stream destination, CancellationToken ct = default)
    {
        var backup = new ConfigurationBackup
        {
            Profiles = [.. await profiles.GetAllAsync(ct)],
            Settings = await settings.GetAsync(ct),
        };

        await JsonSerializer.SerializeAsync(destination, backup, CoreJsonContext.Default.ConfigurationBackup, ct);
    }

    /// <summary>Replaces all profiles and settings. Saved passwords are kept for profiles that still exist.</summary>
    public async Task ImportAsync(Stream source, CancellationToken ct = default)
    {
        var backup = await JsonSerializer.DeserializeAsync(source, CoreJsonContext.Default.ConfigurationBackup, ct)
            ?? throw new InvalidDataException("The backup file is empty.");

        if (backup.FormatVersion > ConfigurationBackup.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"The backup was made by a newer version (format {backup.FormatVersion}).");
        }

        await profiles.ReplaceAllAsync(backup.Profiles, ct);
        await settings.SaveAsync(backup.Settings, ct);
    }
}
