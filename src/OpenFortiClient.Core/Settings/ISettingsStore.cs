namespace OpenFortiClient.Core.Settings;

public interface ISettingsStore
{
    Task<AppSettings> GetAsync(CancellationToken ct = default);

    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
