using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.Core.Tests.Fakes;

/// <summary>macOS VPN services whose states the test sets; records start and stop calls.</summary>
internal sealed class FakeSystemVpns : ISystemVpns
{
    public static readonly SystemVpnService Vpn = new("7A1C2E44-5B6D-4F80-9A3B-2C4D5E6F7081", "VPN", "L2TP");

    private readonly Dictionary<string, SystemVpnState> _states = new() { [Vpn.Id] = SystemVpnState.Disconnected };

    public bool IsAvailable => true;

    public List<string> Calls { get; } = [];

    /// <summary>What StartAsync returns: null starts it (the test then reports the states), else the error.</summary>
    public string? StartError { get; set; }

    public event EventHandler<SystemVpnChange>? Changed;

    public IReadOnlyList<SystemVpnService> Services() => [Vpn];

    public SystemVpnState State(string id) => _states[id];

    public Task<string?> StartAsync(string id)
    {
        Calls.Add($"start {id}");
        return Task.FromResult(StartError);
    }

    public Task<string?> StopAsync(string id)
    {
        Calls.Add($"stop {id}");
        return Task.FromResult<string?>(null);
    }

    public SystemVpnSettings? Settings(string id) => new("vpn.example.com", "user", true, 20, 15);

    public Task<string?> SaveSettingsAsync(string id, SystemVpnSettingsChange change) => Task.FromResult<string?>(null);

    /// <summary>What macOS reports next.</summary>
    public void Set(SystemVpnStatus status, int? lastCause = null)
    {
        var state = new SystemVpnState(status, LastCause: lastCause);
        _states[Vpn.Id] = state;
        Changed?.Invoke(this, new SystemVpnChange(Vpn.Id, state));
    }
}
