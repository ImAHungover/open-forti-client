using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Logging;
using OpenFortiClient.Core.Notifications;
using OpenFortiClient.Core.Settings;

namespace OpenFortiClient.App.Services;

/// <summary>One macOS VPN service as the Connect page and the menu bar show it.</summary>
/// <param name="State">The service's state as the app reports it (a drop is an Error, see <see cref="SystemVpnConnection"/>).</param>
/// <param name="KeepUp">"Keep it up" is on for this service.</param>
/// <param name="PendingReconnect">The reconnect that waits, or null.</param>
public sealed record SystemVpnItem(
    SystemVpnService Service,
    SystemVpnState Current,
    VpnState State,
    bool KeepUp,
    ReconnectSchedule? PendingReconnect);

/// <summary>
/// The VPN services set up in macOS System Settings: their state, switching them on and off, and "Keep it up",
/// which brings a service up when the app starts and reconnects it after a drop. The services stay macOS's own:
/// the app neither copies nor changes their settings, and quitting it leaves them as they are.
/// Empty where <see cref="ISystemVpns"/> is not available (not macOS). See docs/architecture.md, "macOS VPN services".
/// </summary>
public sealed class SystemVpnSession
{
    private const string LogSource = "macos-vpn";

    private readonly ISystemVpns _vpns;
    private readonly ISettingsStore _settings;
    private readonly INotificationHistory _notifications;
    private readonly IAppLog _log;
    private readonly ISystemNotifier _notifier;
    private readonly IHelperControl _helper;
    private readonly Lock _lock = new();
    private IReadOnlyList<Entry> _entries = [];
    private HashSet<string> _keepUp = [];
    private volatile bool _showBanners = true;
    private Task? _initialized;

    public SystemVpnSession(
        ISystemVpns vpns,
        ISettingsStore settings,
        INotificationHistory notifications,
        IAppLog log,
        ISystemNotifier notifier,
        INetworkMonitor network,
        IHelperControl helper)
    {
        _helper = helper;
        _vpns = vpns;
        _settings = settings;
        _notifications = notifications;
        _log = log;
        _notifier = notifier;

        _vpns.Changed += (_, change) => Find(change.Id)?.Connection.OnChanged(change.State);

        // The VPN session starts the monitor. A service's own ppp interface appearing is not a reason to retry it.
        network.Changed += (_, change) =>
        {
            foreach (var entry in _entries)
            {
                if (NetworkChangePolicy.ShouldRetry(change, entry.Connection.State.Status))
                {
                    entry.Supervisor.RetryNow();
                }
            }
        };
    }

    /// <summary>A service's state, its reconnect or its "Keep it up" changed. Raised on a background thread.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<SystemVpnItem> Items
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries.Select(e => new SystemVpnItem(
                    e.Connection.Service, e.Connection.Current, e.Connection.State, _keepUp.Contains(e.Connection.Service.Id), e.Supervisor.Pending))];
            }
        }
    }

    /// <summary>Runs once; later calls return the same task.</summary>
    public Task InitializeAsync() => _initialized ??= Task.Run(InitializeCoreAsync);

    /// <summary>Must not fail, like the VPN session's: problems are logged and the card stays empty.</summary>
    private async Task InitializeCoreAsync()
    {
        AppSettings settings;
        try
        {
            settings = await _settings.GetAsync();
        }
        catch (Exception ex)
        {
            _log.Info(LogSource, $"Could not read the settings, using the defaults: {ex.Message}");
            settings = new AppSettings();
        }

        IReadOnlyList<SystemVpnService> services;
        try
        {
            services = _vpns.IsAvailable ? _vpns.Services() : [];
        }
        catch (Exception ex)
        {
            _log.Info(LogSource, $"Could not list the macOS VPN services: {ex.Message}");
            services = [];
        }

        var entries = services.Select(CreateEntry).ToList();
        lock (_lock)
        {
            _entries = entries;
            _keepUp = [.. settings.KeepUpSystemVpns];
        }

        await ApplySettingsAsync(settings);
        _log.Info(LogSource, services.Count == 0
            ? "No macOS VPN services."
            : $"macOS VPN services: {string.Join(", ", services.Select(s => $"{s.Name} ({s.Kind}, {Find(s.Id)?.Connection.Current.Status})"))}.");

        foreach (var entry in entries.Where(e => IsKeptUp(e.Connection.Service.Id)))
        {
            entry.Supervisor.Arm(alwaysUp: true);
            if (entry.Connection.Current.Status == SystemVpnStatus.Disconnected)
            {
                _log.Info(LogSource, $"Keep it up: starting {entry.Connection.Service.Name}.");
                await RunAsync(entry.Connection.ConnectAsync);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The switch on the card or the line in the menu bar.</summary>
    public async Task SetOnAsync(string id, bool on)
    {
        if (Find(id) is not { } entry)
        {
            return;
        }

        if (on)
        {
            entry.Supervisor.Arm(alwaysUp: IsKeptUp(id));
            await RunAsync(entry.Connection.ConnectAsync);
        }
        else
        {
            // Switched off in the app: no reconnecting until it is switched on again.
            if (entry.Supervisor.Stop())
            {
                Record(entry.Events.OnReconnectStopped(DateTimeOffset.Now));
            }

            await RunAsync(entry.Connection.DisconnectAsync);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The "Keep it up" switch: saved, and on brings the service up now.</summary>
    public async Task SetKeepUpAsync(string id, bool keepUp)
    {
        if (Find(id) is not { } entry)
        {
            return;
        }

        lock (_lock)
        {
            if (keepUp ? !_keepUp.Add(id) : !_keepUp.Remove(id))
            {
                return;
            }
        }

        try
        {
            var settings = await _settings.GetAsync();
            settings.KeepUpSystemVpns = [.. Items.Where(i => i.KeepUp).Select(i => i.Service.Id)];
            await _settings.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            _log.Info(LogSource, $"Could not save Keep it up: {ex.Message}");
        }

        if (keepUp)
        {
            entry.Supervisor.Arm(alwaysUp: true);
            await RunAsync(entry.Connection.ConnectAsync);
        }
        else if (entry.Supervisor.Stop())
        {
            Record(entry.Events.OnReconnectStopped(DateTimeOffset.Now));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The service's editable settings (L2TP only), or null.</summary>
    public SystemVpnSettings? Settings(string id) => Find(id) is null ? null : _vpns.Settings(id);

    /// <summary>
    /// Saves the settings into the macOS service through the privileged helper: checked here first, then by
    /// the helper. A connected service uses them from its next connect.
    /// </summary>
    /// <returns>Null if saved; otherwise what to tell the user.</returns>
    public async Task<string?> SaveSettingsAsync(string id, SystemVpnSettingsChange change)
    {
        if (Find(id) is not { } entry)
        {
            return "This VPN is no longer set up in System Settings.";
        }

        if (change.Validate() is { Count: > 0 } problems)
        {
            return string.Join(" ", problems);
        }

        if (_helper.Status != HelperStatus.Enabled)
        {
            return "Changing a macOS VPN needs the privileged helper. Install and allow it under Settings → Privileged helper.";
        }

        var error = await _vpns.SaveSettingsAsync(id, change);
        if (error is null)
        {
            await AddToHistoryAsync(new NotificationEntry(DateTimeOffset.Now, "macOS VPN", $"Settings of {entry.Connection.Service.Name} changed."));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return error;
    }

    /// <summary>Disconnects and connects again, so the service uses its new settings.</summary>
    public async Task ReconnectAsync(string id)
    {
        if (Find(id) is not { } entry)
        {
            return;
        }

        await RunAsync(entry.Connection.DisconnectAsync);
        for (var i = 0; i < 50 && entry.Connection.Current.Status != SystemVpnStatus.Disconnected; i++)
        {
            await Task.Delay(200);
        }

        await SetOnAsync(id, on: true);
    }

    public Task ApplySettingsAsync(AppSettings settings)
    {
        _showBanners = settings.ShowSystemNotifications;
        var delays = settings.ReconnectDelaysSeconds is { Count: > 0 } seconds
            ? [.. seconds.Select(s => TimeSpan.FromSeconds(Math.Max(1, s)))]
            : ReconnectSupervisor.DefaultDelays;
        foreach (var entry in _entries)
        {
            entry.Supervisor.Delays = delays;
        }

        return Task.CompletedTask;
    }

    private Entry CreateEntry(SystemVpnService service)
    {
        var connection = new SystemVpnConnection(_vpns, service);
        var events = ConnectionEvents.ForSystemVpn(service.Name, connection.State.Status);
        connection.StateChanged += (_, state) =>
        {
            _log.Info(LogSource, $"{service.Name}: {state.Status} {state.Error?.Message}".TrimEnd());
            Record(events.OnStateChanged(state, DateTimeOffset.Now));
            Changed?.Invoke(this, EventArgs.Empty);
        };

        // Subscribed after the handler above, so a drop is recorded as lost before the reconnect is scheduled.
        var supervisor = new ReconnectSupervisor(connection, connection.ConnectAsync);
        supervisor.Scheduled += (_, schedule) =>
        {
            Record(events.OnReconnectScheduled(schedule, host: null, DateTimeOffset.Now));
            Changed?.Invoke(this, EventArgs.Empty);
        };
        supervisor.Paused += (_, reason) => Record(events.OnReconnectPaused(reason, DateTimeOffset.Now));
        supervisor.Ended += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        return new Entry(connection, supervisor, events);
    }

    private Entry? Find(string id) => _entries.FirstOrDefault(e => e.Connection.Service.Id == id);

    private bool IsKeptUp(string id)
    {
        lock (_lock)
        {
            return _keepUp.Contains(id);
        }
    }

    /// <summary>macOS calls block briefly; never on the UI thread, and never let them fail the caller.</summary>
    private async Task RunAsync(Func<Task> work)
    {
        try
        {
            await Task.Run(work);
        }
        catch (Exception ex)
        {
            _log.Info(LogSource, $"macOS VPN call failed: {ex.Message}");
        }
    }

    private Task RunAsync(Func<CancellationToken, Task> work) => RunAsync(() => work(CancellationToken.None));

    /// <summary>History row always; banner when the setting allows it, as for the VPN.</summary>
    private void Record(ConnectionEvent? connectionEvent)
    {
        if (connectionEvent is null)
        {
            return;
        }

        _ = AddToHistoryAsync(connectionEvent.Entry);
        if (connectionEvent.Banner is { } banner && _showBanners)
        {
            _ = _notifier.ShowAsync(banner);
        }
    }

    private async Task AddToHistoryAsync(NotificationEntry entry)
    {
        try
        {
            await _notifications.AddAsync(entry);
        }
        catch (Exception ex)
        {
            _log.Info(LogSource, $"Could not add to the notification history ({entry.Message}): {ex.Message}");
        }
    }

    private sealed record Entry(SystemVpnConnection Connection, ReconnectSupervisor Supervisor, ConnectionEvents Events);
}
