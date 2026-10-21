using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.Core.Notifications;

/// <summary>A macOS notification banner.</summary>
public sealed record Banner(string Title, string Body, NotificationSeverity Severity);

/// <summary>What one connection event adds: a row in the history, and maybe a banner.</summary>
public sealed record ConnectionEvent(NotificationEntry Entry, Banner? Banner);

/// <summary>
/// Decides which engine events reach the Notifications page and which also show a macOS banner.
/// Banners are for what the user may not be watching: connected, lost, failed, failover; not for a
/// disconnect they asked for. Keeps track of the current attempt, so create one per connection:
/// the engine, and each macOS VPN service (<see cref="ForSystemVpn"/>).
/// See docs/architecture.md, "Logs and notifications".
/// </summary>
public sealed class ConnectionEvents
{
    private readonly string _source;
    private readonly string _title;
    private readonly string _keepUp;
    private readonly string? _target;

    private VpnStatus _last = VpnStatus.Disconnected;
    private bool _reachedConnected;
    private bool _reconnecting;

    /// <summary>For the VPN engine: "Connected to vpn1.example.com.", "VPN connection lost", "Always up paused".</summary>
    public ConnectionEvents()
        : this("VPN", "VPN", "Always up", target: null)
    {
    }

    private ConnectionEvents(string source, string title, string keepUp, string? target)
    {
        _source = source;
        _title = title;
        _keepUp = keepUp;
        _target = target;
    }

    /// <summary>For a macOS VPN service: "Connected to VPN.", "macOS VPN connection lost", "Keep it up paused".</summary>
    /// <param name="current">Its state when the app starts: it may already be up.</param>
    public static ConnectionEvents ForSystemVpn(string name, VpnStatus current = VpnStatus.Disconnected) =>
        new("macOS VPN", "macOS VPN", "Keep it up", name)
        {
            _last = current,
            _reachedConnected = current == VpnStatus.Connected,
        };

    public ConnectionEvent? OnStateChanged(VpnState state, DateTimeOffset now)
    {
        var previous = _last;
        _last = state.Status;

        if (state.Status == VpnStatus.Connecting && previous is VpnStatus.Disconnected or VpnStatus.Error)
        {
            _reachedConnected = false;
        }

        switch (state.Status)
        {
            case VpnStatus.Connected:
                _reachedConnected = true;
                if (_reconnecting)
                {
                    _reconnecting = false;
                    var reconnected = $"Reconnected to {Target(state)}.";
                    return new ConnectionEvent(new NotificationEntry(now, _source, reconnected), new Banner($"{_title} reconnected", reconnected, NotificationSeverity.Info));
                }

                var connected = $"Connected to {Target(state)}.";
                return new ConnectionEvent(new NotificationEntry(now, _source, connected), new Banner($"{_title} connected", connected, NotificationSeverity.Info));

            case VpnStatus.Disconnected when _reachedConnected:
                _reachedConnected = false;
                return new ConnectionEvent(new NotificationEntry(now, _source, _target is null ? "Disconnected." : $"Disconnected from {_target}."), null);

            case VpnStatus.Disconnected when previous == VpnStatus.Disconnecting:
                return new ConnectionEvent(new NotificationEntry(now, _source, "Connection attempt cancelled."), null);

            case VpnStatus.Error when state.Error is { } error:
                // A new attempt can fail without passing through Connecting (an invalid profile).
                if (previous is VpnStatus.Error or VpnStatus.Disconnected)
                {
                    _reachedConnected = false;
                }

                var entry = new NotificationEntry(now, _source, error.Message, NotificationSeverity.Error);
                var title = previous switch
                {
                    VpnStatus.Connected => $"{_title} connection lost",
                    VpnStatus.Disconnecting => $"{_title} did not disconnect cleanly",
                    _ => $"{_title} connection failed",
                };

                // The app asks about the certificate right away, so a banner would only repeat the question.
                // While reconnecting, the "lost" banner was already shown; failed attempts only go to the history.
                var quiet = error.Kind == VpnErrorKind.UntrustedCertificate || (_reconnecting && previous != VpnStatus.Connected);
                Banner? banner = quiet ? null : new Banner(title, error.Message, NotificationSeverity.Error);
                return new ConnectionEvent(entry, banner);

            default:
                return null;
        }
    }

    public ConnectionEvent OnReconnectScheduled(ReconnectSchedule schedule, string? host, DateTimeOffset now)
    {
        _reconnecting = true;
        return new ConnectionEvent(
            new NotificationEntry(now, _source, $"Reconnecting to {_target ?? host} in {schedule.Delay.TotalSeconds:0} s (attempt {schedule.Attempt})."),
            null);
    }

    /// <summary>The user stopped reconnecting.</summary>
    public ConnectionEvent OnReconnectStopped(DateTimeOffset now)
    {
        _reconnecting = false;
        return new ConnectionEvent(new NotificationEntry(now, _source, "Reconnecting stopped."), null);
    }

    /// <summary>A manual connect replaced a waiting reconnect: nothing to record.</summary>
    public void OnReconnectReplaced() => _reconnecting = false;

    public ConnectionEvent OnReconnectPaused(string reason, DateTimeOffset now)
    {
        _reconnecting = false;
        return new ConnectionEvent(
            new NotificationEntry(now, _source, $"{_keepUp} paused: {reason}", NotificationSeverity.Warning),
            new Banner($"{_keepUp} paused", reason, NotificationSeverity.Warning));
    }

    /// <summary>Only a failover is recorded; after the last gateway the Error state reports the failure.</summary>
    public static ConnectionEvent? OnGatewayFailed(GatewayFailure failure, DateTimeOffset now)
    {
        if (failure.Next is null)
        {
            return null;
        }

        var message = $"{failure.Gateway.Host} failed ({failure.Error.Message.TrimEnd('.')}). Trying {failure.Next.Host}.";
        return new ConnectionEvent(
            new NotificationEntry(now, "VPN", message, NotificationSeverity.Warning),
            new Banner("Trying another gateway", message, NotificationSeverity.Warning));
    }

    private string? Target(VpnState state) => _target ?? state.Gateway?.Host;
}
