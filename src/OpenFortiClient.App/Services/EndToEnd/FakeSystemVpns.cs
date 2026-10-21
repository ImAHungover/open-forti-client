#if E2E
using System.Text.Json.Nodes;
using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.App.Services.EndToEnd;

/// <summary>
/// macOS VPN services for the UI tests, from <c>system-vpns.json</c> in the test folder
/// (<c>{"services":[{"id":…,"name":…,"kind":…,"status":"connected"}]}</c>; no file, no services).
/// Start connects after a moment, Stop disconnects; both are logged to <c>system-vpn.log</c>. The test makes
/// macOS's side happen with AppSession.Trigger: <c>system-vpn-drop</c> (the connection is lost),
/// <c>system-vpn-off-elsewhere</c> (switched off in System Settings), and <c>system-vpn-start-fails</c>
/// (macOS refuses the next start, as for a service without saved credentials).
/// L2TP services have settings (server vpn.example.com, account user, send all traffic, keep-alive 20 s × 15);
/// a save is logged as "save {id} {server} {account} …" with secrets as *** and kept for the next read.
/// </summary>
public sealed class FakeSystemVpns : ISystemVpns
{
    private const int PeerDead = 9;
    private readonly string _directory;
    private readonly Lock _lock = new();
    private readonly List<SystemVpnService> _services = [];
    private readonly Dictionary<string, SystemVpnState> _states = [];
    private readonly Dictionary<string, SystemVpnSettings> _settings = [];

    public FakeSystemVpns(string directory)
    {
        _directory = directory;
        var file = Path.Combine(directory, "system-vpns.json");
        if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file))?["services"] is JsonArray services)
        {
            foreach (var service in services.OfType<JsonObject>())
            {
                var id = (string)service["id"]!;
                _services.Add(new SystemVpnService(id, (string)service["name"]!, (string?)service["kind"] ?? "L2TP"));
                _states[id] = (string?)service["status"] == "connected" ? Up() : SystemVpnState.Disconnected;
                if (_services[^1].Kind == "L2TP")
                {
                    _settings[id] = new SystemVpnSettings("vpn.example.com", "user", SendAllTraffic: true, EchoIntervalSeconds: 20, EchoFailures: 15);
                }
            }
        }

        _ = WatchAsync();
    }

    public bool IsAvailable => true;

    public event EventHandler<SystemVpnChange>? Changed;

    public IReadOnlyList<SystemVpnService> Services() => _services;

    public SystemVpnState State(string id)
    {
        lock (_lock)
        {
            return _states.GetValueOrDefault(id, SystemVpnState.Disconnected);
        }
    }

    public Task<string?> StartAsync(string id)
    {
        Log($"start {id}");
        if (Take("system-vpn-start-fails"))
        {
            return Task.FromResult<string?>("No service");
        }

        Set(id, new SystemVpnState(SystemVpnStatus.Connecting));
        _ = Task.Delay(300).ContinueWith(_ => Set(id, Up()));
        return Task.FromResult<string?>(null);
    }

    public SystemVpnSettings? Settings(string id)
    {
        lock (_lock)
        {
            return _settings.GetValueOrDefault(id);
        }
    }

    public Task<string?> SaveSettingsAsync(string id, SystemVpnSettingsChange change)
    {
        var s = change.Settings;
        Log($"save {id} {s.Server} {s.Account} all={s.SendAllTraffic} echo={s.EchoIntervalSeconds}x{s.EchoFailures}"
            + $" password={(change.Password is null ? "kept" : "***")} secret={(change.SharedSecret is null ? "kept" : "***")}");
        lock (_lock)
        {
            _settings[id] = s;
        }

        return Task.FromResult<string?>(null);
    }

    public Task<string?> StopAsync(string id)
    {
        Log($"stop {id}");
        Set(id, new SystemVpnState(SystemVpnStatus.Disconnecting));
        _ = Task.Delay(200).ContinueWith(_ => Set(id, new SystemVpnState(SystemVpnStatus.Disconnected, LastCause: SystemVpnCause.SwitchedOff)));
        return Task.FromResult<string?>(null);
    }

    private static SystemVpnState Up() => new(SystemVpnStatus.Connected, DateTimeOffset.Now, "192.168.2.2");

    private void Set(string id, SystemVpnState state)
    {
        lock (_lock)
        {
            _states[id] = state;
        }

        Changed?.Invoke(this, new SystemVpnChange(id, state));
    }

    private void Log(string line)
    {
        lock (_lock)
        {
            File.AppendAllText(Path.Combine(_directory, "system-vpn.log"), line + Environment.NewLine);
        }
    }

    private async Task WatchAsync()
    {
        while (true)
        {
            await Task.Delay(100);
            if (Take("system-vpn-drop"))
            {
                Disconnect(PeerDead);
            }

            if (Take("system-vpn-off-elsewhere"))
            {
                Disconnect(SystemVpnCause.SwitchedOff);
            }
        }
    }

    private void Disconnect(int lastCause)
    {
        foreach (var service in _services.Where(s => State(s.Id).Status == SystemVpnStatus.Connected))
        {
            Set(service.Id, new SystemVpnState(SystemVpnStatus.Disconnected, LastCause: lastCause));
        }
    }

    private bool Take(string name)
    {
        var path = Path.Combine(_directory, name);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }
}
#endif
