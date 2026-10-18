namespace OpenFortiClient.Core.Profiles;

public interface IProfileStore
{
    Task<IReadOnlyList<VpnProfile>> GetAllAsync(CancellationToken ct = default);

    Task<VpnProfile?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Adds the profile, or replaces the one with the same id.</summary>
    Task SaveAsync(VpnProfile profile, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Replaces all profiles, used by backup restore.</summary>
    Task ReplaceAllAsync(IReadOnlyList<VpnProfile> profiles, CancellationToken ct = default);
}
