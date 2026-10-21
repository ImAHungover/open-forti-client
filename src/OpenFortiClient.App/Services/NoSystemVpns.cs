using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.App.Services;

/// <summary>Where there are no macOS VPN services: not on macOS. The Connect page then shows no macOS VPN card.</summary>
public sealed class NoSystemVpns : ISystemVpns
{
    public bool IsAvailable => false;

    public event EventHandler<SystemVpnChange>? Changed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<SystemVpnService> Services() => [];

    public SystemVpnState State(string id) => SystemVpnState.Disconnected;

    public Task<string?> StartAsync(string id) => Task.FromResult<string?>("macOS VPN services are only available on macOS.");

    public Task<string?> StopAsync(string id) => Task.FromResult<string?>(null);

    public SystemVpnSettings? Settings(string id) => null;

    public Task<string?> SaveSettingsAsync(string id, SystemVpnSettingsChange change) =>
        Task.FromResult<string?>("macOS VPN services are only available on macOS.");
}
