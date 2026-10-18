using CoreFoundation;
using Network;
using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Logging;

namespace OpenFortiClient.App;

/// <summary>
/// Network changes from NWPathMonitor, collected over 2 s: a switch of network comes as a burst of
/// updates. Reports what changed; <see cref="NetworkChangePolicy"/> decides whether it matters.
/// </summary>
public sealed class PathNetworkMonitor(IAppLog log) : INetworkMonitor, IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);

    private readonly NWPathMonitor _monitor = new();
    private readonly Lock _lock = new();
    private HashSet<string>? _interfaces;
    private bool _usable;
    private bool _becameUsable;
    private readonly HashSet<string> _added = [];
    private readonly HashSet<string> _removed = [];
    private Timer? _timer;

    public event EventHandler<NetworkChange>? Changed;

    public void Start()
    {
        _monitor.SetQueue(new DispatchQueue("io.github.openforticlient.network"));
        _monitor.SnapshotHandler = OnPath;
        _monitor.Start();
    }

    private void OnPath(NWPath path)
    {
        var interfaces = new HashSet<string>();
        path.EnumerateInterfaces(i =>
        {
            interfaces.Add(i.Name ?? "?");
            return true;
        });
        var usable = path.Status == NWPathStatus.Satisfied;

        lock (_lock)
        {
            // The first snapshot is the starting point, not a change.
            if (_interfaces is null)
            {
                _interfaces = interfaces;
                _usable = usable;
                return;
            }

            _becameUsable |= usable && !_usable;
            foreach (var name in interfaces.Except(_interfaces))
            {
                _removed.Remove(name);
                _added.Add(name);
            }

            foreach (var name in _interfaces.Except(interfaces))
            {
                _added.Remove(name);
                _removed.Add(name);
            }

            _interfaces = interfaces;
            _usable = usable;
            _timer?.Dispose();
            _timer = new Timer(_ => Report(), null, Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Report()
    {
        NetworkChange change;
        lock (_lock)
        {
            change = new NetworkChange(_becameUsable, [.. _added.Order()], [.. _removed.Order()]);
            _becameUsable = false;
            _added.Clear();
            _removed.Clear();
        }

        if (change.BecameUsable || change.Added.Count > 0 || change.Removed.Count > 0)
        {
            log.Info("network", $"Network changed: usable again {change.BecameUsable}, added [{string.Join(", ", change.Added)}], removed [{string.Join(", ", change.Removed)}].");
            Changed?.Invoke(this, change);
        }
    }

    public void Dispose()
    {
        _monitor.Cancel();
        _timer?.Dispose();
    }
}
