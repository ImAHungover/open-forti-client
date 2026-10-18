using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.Core.Notifications;

/// <summary>A macOS notification banner.</summary>
public sealed record Banner(string Title, string Body, NotificationSeverity Severity);

/// <summary>What one connection event adds: a row in the history, and maybe a banner.</summary>
public sealed record ConnectionEvent(NotificationEntry Entry, Banner? Banner);

/// <summary>
/// Decides which engine events reach the Notifications page and which also show a macOS banner.
/// Banners are for what the user may not be watching: connected, lost, failed, failover; not for a
/// disconnect they asked for. Keeps track of the current attempt, so create one per engine.
/// See docs/architecture.md, "Logs and notifications".
/// </summary>
public sealed class ConnectionEvents
{
    private const string Source = "VPN";

    private VpnStatus _last = VpnStatus.Disconnected;
    private bool _reachedConnected;
    private bool _reconnecting;

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
                    var reconnected = $"Reconnected to {state.Gateway?.Host}.";
                    return new ConnectionEvent(new NotificationEntry(now, Source, reconnected), new Banner("VPN reconnected", reconnected, NotificationSeverity.Info));
                }

                var connected = $"Connected to {state.Gateway?.Host}.";
                return new ConnectionEvent(new NotificationEntry(now, Source, connected), new Banner("VPN connected", connected, NotificationSeverity.Info));

            case VpnStatus.Disconnected when _reachedConnected:
                _reachedConnected = false;
                return new ConnectionEvent(new NotificationEntry(now, Source, "Disconnected."), null);

            case VpnStatus.Disconnected when previous == VpnStatus.Disconnecting:
                return new ConnectionEvent(new NotificationEntry(now, Source, "Connection attempt cancelled."), null);

            case VpnStatus.Error when state.Error is { } error:
                // A new attempt can fail without passing through Connecting (an invalid profile).
                if (previous is VpnStatus.Error or VpnStatus.Disconnected)
                {
                    _reachedConnected = false;
                }

                var entry = new NotificationEntry(now, Source, error.Message, NotificationSeverity.Error);
                var title = previous switch
                {
                    VpnStatus.Connected => "VPN connection lost",
                    VpnStatus.Disconnecting => "VPN did not disconnect cleanly",
                    _ => "VPN connection failed",
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
            new NotificationEntry(now, Source, $"Reconnecting to {host} in {schedule.Delay.TotalSeconds:0} s (attempt {schedule.Attempt})."),
            null);
    }

    /// <summary>The user stopped reconnecting.</summary>
    public ConnectionEvent OnReconnectStopped(DateTimeOffset now)
    {
        _reconnecting = false;
        return new ConnectionEvent(new NotificationEntry(now, Source, "Reconnecting stopped."), null);
    }

    /// <summary>A manual connect replaced a waiting reconnect: nothing to record.</summary>
    public void OnReconnectReplaced() => _reconnecting = false;

    public ConnectionEvent OnReconnectPaused(string reason, DateTimeOffset now)
    {
        _reconnecting = false;
        return new ConnectionEvent(
            new NotificationEntry(now, Source, $"Always up paused: {reason}", NotificationSeverity.Warning),
            new Banner("Always up paused", reason, NotificationSeverity.Warning));
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
            new NotificationEntry(now, Source, message, NotificationSeverity.Warning),
            new Banner("Trying another gateway", message, NotificationSeverity.Warning));
    }
}
