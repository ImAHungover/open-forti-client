namespace OpenFortiClient.Core.Profiles;

/// <summary>Stores profiles in <c>profiles.json</c>.</summary>
public sealed class JsonProfileStore(AppPaths paths) : IProfileStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlyList<VpnProfile>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await LoadAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<VpnProfile?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var profiles = await GetAllAsync(ct);
        return profiles.FirstOrDefault(p => p.Id == id);
    }

    public Task SaveAsync(VpnProfile profile, CancellationToken ct = default) =>
        UpdateAsync(profiles =>
        {
            // Only one connection connects when the app starts.
            if (profile.AutoConnect)
            {
                for (var i = 0; i < profiles.Count; i++)
                {
                    if (profiles[i].Id != profile.Id && profiles[i].AutoConnect)
                    {
                        profiles[i] = profiles[i] with { AutoConnect = false };
                    }
                }
            }

            var index = profiles.FindIndex(p => p.Id == profile.Id);
            if (index >= 0)
            {
                profiles[index] = profile;
            }
            else
            {
                profiles.Add(profile);
            }
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        UpdateAsync(profiles => profiles.RemoveAll(p => p.Id == id), ct);

    public Task ReplaceAllAsync(IReadOnlyList<VpnProfile> profiles, CancellationToken ct = default) =>
        UpdateAsync(current =>
        {
            current.Clear();
            current.AddRange(profiles);
        }, ct);

    private async Task UpdateAsync(Action<List<VpnProfile>> change, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var profiles = await LoadAsync(ct);
            change(profiles);
            await JsonFile.WriteAsync(paths.ProfilesFile, profiles, CoreJsonContext.Default.ListVpnProfile, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<VpnProfile>> LoadAsync(CancellationToken ct) =>
        await JsonFile.ReadAsync(paths.ProfilesFile, CoreJsonContext.Default.ListVpnProfile, ct) ?? [];
}
