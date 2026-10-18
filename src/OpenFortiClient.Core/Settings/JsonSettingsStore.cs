namespace OpenFortiClient.Core.Settings;

/// <summary>Stores settings in <c>settings.json</c>.</summary>
public sealed class JsonSettingsStore(AppPaths paths) : ISettingsStore
{
    public async Task<AppSettings> GetAsync(CancellationToken ct = default) =>
        await JsonFile.ReadAsync(paths.SettingsFile, CoreJsonContext.Default.AppSettings, ct) ?? new AppSettings();

    public Task SaveAsync(AppSettings settings, CancellationToken ct = default) =>
        JsonFile.WriteAsync(paths.SettingsFile, settings, CoreJsonContext.Default.AppSettings, ct);
}
