using OpenFortiClient.Core;
using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Engine.OpenFortiVpn;
using OpenFortiClient.Core.Logging;
using OpenFortiClient.Core.Notifications;
using OpenFortiClient.Core.Profiles;
using OpenFortiClient.Core.Secrets;
using OpenFortiClient.Core.Settings;

namespace OpenFortiClient.App.Services;

/// <summary>
/// The app's single entry point for connecting. Wraps the engine with what the UI needs around it:
/// Keychain passwords, the OTP prompt, the certificate trust prompt, logging and notifications.
/// </summary>
public sealed class VpnSession
{
    private const string LogSource = "openfortivpn";

    private readonly OpenFortiVpnEngine _engine;
    private readonly IProfileStore _profiles;
    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;
    private readonly INotificationHistory _notifications;
    private readonly IAppLog _log;
    private readonly IDialogService _dialogs;
    private readonly AppPaths _paths;
    private readonly ISystemNotifier _notifier;
    private readonly ConnectionEvents _events = new();
    private readonly ReconnectSupervisor _supervisor;
    private readonly INetworkMonitor _network;
    private readonly ElevationLauncher _elevation;
    private volatile bool _showBanners = true;
    private Task? _initialized;

    // What the last manual connect used, so "always up" can log in again. In memory only.
    private (Guid ProfileId, VpnCredentials Credentials)? _lastAttempt;

    // The gateway of the last connection, for "Reconnecting to {host}" once the state no longer names one.
    private string? _lastHost;

    public VpnSession(
        OpenFortiVpnEngine engine,
        IProfileStore profiles,
        ISettingsStore settings,
        ISecretStore secrets,
        INotificationHistory notifications,
        IAppLog log,
        IDialogService dialogs,
        AppPaths paths,
        ISystemNotifier notifier,
        INetworkMonitor network,
        ElevationLauncher elevation)
    {
        _elevation = elevation;
        _engine = engine;
        _profiles = profiles;
        _settings = settings;
        _secrets = secrets;
        _notifications = notifications;
        _log = log;
        _dialogs = dialogs;
        _paths = paths;
        _notifier = notifier;

        _engine.LogLine += (_, line) => _log.Info(LogSource, line);
        _engine.StateChanged += OnEngineStateChanged;
        _engine.GatewayFailed += (_, failure) => Record(ConnectionEvents.OnGatewayFailed(failure, DateTimeOffset.Now));

        // Subscribed after the session's own handler, so a drop is recorded as lost before the reconnect is scheduled.
        _supervisor = new ReconnectSupervisor(_engine, ReconnectAsync);
        _supervisor.Scheduled += (_, schedule) =>
        {
            Record(_events.OnReconnectScheduled(schedule, _lastHost, DateTimeOffset.Now));
            ReconnectChanged?.Invoke(this, EventArgs.Empty);
        };
        _supervisor.Paused += (_, reason) => Record(_events.OnReconnectPaused(reason, DateTimeOffset.Now));
        _supervisor.Ended += (_, _) => ReconnectChanged?.Invoke(this, EventArgs.Empty);

        _network = network;
        _network.Changed += (_, change) =>
        {
            if (NetworkChangePolicy.ShouldRetry(change, _engine.State.Status))
            {
                _supervisor.RetryNow();
            }
        };
    }

    /// <summary>Raised on a background thread.</summary>
    public event EventHandler<VpnState>? StateChanged;

    public VpnState State => _engine.State;

    /// <summary>Waiting for, or running, an automatic reconnect ("always up").</summary>
    public bool IsReconnecting => _supervisor.IsActive;

    /// <summary>The automatic reconnect that is waiting, or null.</summary>
    public ReconnectSchedule? PendingReconnect => _supervisor.Pending;

    /// <summary>A reconnect was scheduled, or reconnecting ended. Raised on a background thread.</summary>
    public event EventHandler? ReconnectChanged;

    /// <summary>Runs once; later calls return the same task.</summary>
    public Task InitializeAsync() => _initialized ??= InitializeCoreAsync();

    /// <summary>
    /// Must not fail: auto-connect awaits it, and the task is kept. Each step logs its own problem and the
    /// others still run.
    /// </summary>
    private async Task InitializeCoreAsync()
    {
        try
        {
            _network.Start();
        }
        catch (Exception ex)
        {
            _log.Info("session", $"Network changes will not be noticed: {ex.Message}");
        }

        try
        {
            DeleteLeftoverConfigFiles();
        }
        catch (Exception ex)
        {
            _log.Info("session", $"Could not remove a leftover config file: {ex.Message}");
        }

        AppSettings settings;
        try
        {
            settings = await _settings.GetAsync();
        }
        catch (Exception ex)
        {
            _log.Info("session", $"Could not read the settings, using the defaults: {ex.Message}");
            settings = new AppSettings();
        }

        await ApplySettingsAsync(settings);
    }

    /// <summary>Connects from the menu bar, which has no password field.</summary>
    /// <returns>false when the profile has no saved password, so the window has to ask for it.</returns>
    public async Task<bool> ConnectFromMenuAsync(VpnProfile profile)
    {
        var password = await GetSavedPasswordAsync(profile);
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        await ConnectAsync(profile, profile.Username, password, savePassword: true);
        return true;
    }

    /// <summary>Connects a profile marked "connect when OpenFortiClient starts", if its password is saved.</summary>
    public async Task AutoConnectAsync(VpnProfile profile)
    {
        await InitializeAsync();
        if (_engine.State.Status != VpnStatus.Disconnected)
        {
            return;
        }

        var password = await GetSavedPasswordAsync(profile);
        if (string.IsNullOrEmpty(password))
        {
            _ = AddToHistoryAsync(new NotificationEntry(DateTimeOffset.Now, "VPN", $"Auto-connect skipped: {profile.Name} needs a password."));
            return;
        }

        _log.Info("session", $"Auto-connecting {profile.Name}.");
        await ConnectAsync(profile, profile.Username, password, savePassword: true);
    }

    public Task ApplySettingsAsync(AppSettings settings)
    {
        _log.MinimumLevel = settings.LogLevel;
        _engine.Options = OpenFortiVpnEngineOptions.From(settings, _paths);
        _showBanners = settings.ShowSystemNotifications;
        _elevation.UseHelper = settings.UsePrivilegedHelper;
        _supervisor.Delays = settings.ReconnectDelaysSeconds is { Count: > 0 } seconds
            ? [.. seconds.Select(s => TimeSpan.FromSeconds(Math.Max(1, s)))]
            : ReconnectSupervisor.DefaultDelays;

        // Asks the user only the first time. Not awaited: nothing should wait for the user's answer.
        if (settings.ShowSystemNotifications)
        {
            _ = _notifier.RequestPermissionAsync();
        }

        return Task.CompletedTask;
    }

    /// <summary>Removes the saved password, for example when a connection stops saving its login.</summary>
    public Task ForgetPasswordAsync(Guid profileId) => ForgetAsync(SecretKeys.Password(profileId), "saved password");

    /// <summary>Removes the client key from the Keychain.</summary>
    public Task ForgetClientKeyAsync(Guid profileId) => ForgetAsync(SecretKeys.ClientKey(profileId), "client key");

    /// <summary>Stores a client private key (PEM) in the Keychain. Throws if that fails: the save must not pretend.</summary>
    public Task StoreClientKeyAsync(Guid profileId, string pem) => _secrets.SetAsync(SecretKeys.ClientKey(profileId), pem);

    private async Task ForgetAsync(string key, string what)
    {
        try
        {
            await _secrets.RemoveAsync(key);
        }
        catch (Exception ex)
        {
            _log.Info("session", $"Could not remove the {what} from the Keychain: {ex.Message}");
        }
    }

    private async Task<string?> GetClientKeyAsync(VpnProfile profile)
    {
        if (!profile.ClientKeyInKeychain)
        {
            return null;
        }

        try
        {
            return await _secrets.GetAsync(SecretKeys.ClientKey(profile.Id));
        }
        catch (Exception ex)
        {
            _log.Info("session", $"Could not read the client key from the Keychain: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GetSavedPasswordAsync(VpnProfile profile)
    {
        if (profile.AuthMode != AuthMode.SaveLogin)
        {
            return null;
        }

        try
        {
            return await _secrets.GetAsync(SecretKeys.Password(profile.Id));
        }
        catch (Exception ex)
        {
            _log.Info("session", $"Could not read the saved password from the Keychain: {ex.Message}");
            return null;
        }
    }

    public async Task ConnectAsync(VpnProfile profile, string? username, string? password, bool savePassword)
    {
        // The Connect screen may hold an older copy, for example from before a certificate was trusted.
        profile = await _profiles.GetAsync(profile.Id) ?? profile;

        // A manual connect replaces any waiting reconnect, and decides whether drops reconnect from now on.
        if (_supervisor.Arm(profile.AlwaysUp))
        {
            _events.OnReconnectReplaced();
        }

        if (string.IsNullOrEmpty(password))
        {
            password = await GetSavedPasswordAsync(profile);
        }

        if (!savePassword && profile.AuthMode == AuthMode.SaveLogin)
        {
            await ForgetPasswordAsync(profile.Id);
        }
        else if (savePassword && !string.IsNullOrEmpty(password))
        {
            // A Keychain failure (for example an unsigned build without keychain-access-groups) must not block connecting.
            try
            {
                await _secrets.SetAsync(SecretKeys.Password(profile.Id), password);
            }
            catch (Exception ex)
            {
                _log.Info("session", $"Could not save the password to the Keychain: {ex.Message}");
            }
        }

        var credentials = new VpnCredentials(username, password)
        {
            OtpProvider = _ => _dialogs.PromptAsync(
                "One-time password",
                $"{profile.Name} asks for a one-time password.",
                Keyboard.Numeric),
            ClientKeyPem = await GetClientKeyAsync(profile),
        };

        _lastAttempt = (profile.Id, credentials);
        await RunEngineAsync(() => _engine.ConnectAsync(profile, credentials));

        if (State.Error is { Kind: VpnErrorKind.UntrustedCertificate, CertificateDigest: { } digest }
            && await ShouldTrustAsync(profile, digest))
        {
            var trusted = profile with { TrustedCertDigests = [.. profile.TrustedCertDigests, digest] };
            await _profiles.SaveAsync(trusted);
            await RunEngineAsync(() => _engine.ConnectAsync(trusted, credentials));
        }
    }

    /// <summary>Disconnects, and stops any automatic reconnect ("Stop reconnecting" on the Connect screen).</summary>
    public async Task DisconnectAsync()
    {
        StopReconnecting();
        await RunEngineAsync(_engine.DisconnectAsync);
    }

    /// <summary>A connection was saved: a running "always up" stops if that connection no longer wants it.</summary>
    public void OnProfileSaved(VpnProfile profile)
    {
        if (_lastAttempt?.ProfileId == profile.Id && !profile.AlwaysUp)
        {
            StopReconnecting();
        }
    }

    /// <summary>The one way to stop "always up", so the history always says so.</summary>
    private void StopReconnecting()
    {
        if (_supervisor.Stop())
        {
            Record(_events.OnReconnectStopped(DateTimeOffset.Now));
        }
    }

    /// <summary>
    /// Engine work runs on the thread pool, never on the UI thread's context. Otherwise its awaits resume
    /// on the main thread, and anything that blocks the main thread (quitting) would stop the engine too.
    /// </summary>
    private static Task RunEngineAsync(Func<Task> work) => Task.Run(work);

    /// <summary>
    /// The app is quitting (menu bar, Cmd-Q, logout): stop openfortivpn so it removes its routes and DNS
    /// settings, instead of leaving it running as root. Blocks, because the process ends right after.
    /// </summary>
    public void DisconnectBeforeQuit()
    {
        if (_engine.State.Status is VpnStatus.Disconnected && !_supervisor.IsActive)
        {
            return;
        }

        _log.Info("session", "Quitting: disconnecting first.");
        _supervisor.Stop();
        if (!Task.Run(_engine.DisconnectAsync).Wait(TimeSpan.FromSeconds(25)))
        {
            _log.Info("session", "openfortivpn did not stop before quitting.");
        }
    }

    /// <summary>Called by the supervisor: the last profile (as stored now) with the last credentials.</summary>
    private async Task ReconnectAsync(CancellationToken ct)
    {
        // The connection may have been deleted, or "always up" turned off, while the reconnect waited.
        if (_lastAttempt is not { } last || await _profiles.GetAsync(last.ProfileId, ct) is not { AlwaysUp: true } profile)
        {
            StopReconnecting();
            return;
        }

        _log.Info("session", $"Reconnecting {profile.Name} (always up).");
        await RunEngineAsync(() => _engine.ConnectAsync(profile, last.Credentials, ct));
    }

    public async Task DeleteProfileAsync(Guid profileId)
    {
        // First, so a reconnect cannot start for a connection that is being deleted.
        if (_lastAttempt?.ProfileId == profileId)
        {
            StopReconnecting();
        }

        await _profiles.DeleteAsync(profileId);
        await ForgetPasswordAsync(profileId);
        await ForgetClientKeyAsync(profileId);
    }

    private async Task<bool> ShouldTrustAsync(VpnProfile profile, string digest)
    {
        var settings = await _settings.GetAsync();
        if (!settings.WarnInvalidServerCertificate)
        {
            _log.Info("session", $"Trusting certificate {digest} for {profile.Name} without asking (setting).");
            return true;
        }

        return await _dialogs.ConfirmAsync(
            "Untrusted certificate",
            $"The gateway for {profile.Name} presented a certificate that is not trusted.\n\n" +
            $"SHA-256: {digest}\n\n" +
            "Only trust it if your administrator confirmed this fingerprint.",
            accept: "Trust and connect",
            cancel: "Cancel");
    }

    private void OnEngineStateChanged(object? sender, VpnState state)
    {
        if (state.Gateway?.Host is { } host)
        {
            _lastHost = host;
        }

        _log.Info("session", $"State: {state.Status} {state.Gateway?.Host} {state.Error?.Message}".TrimEnd());
        Record(_events.OnStateChanged(state, DateTimeOffset.Now));
        StateChanged?.Invoke(this, state);
    }

    /// <summary>History row always; banner when the setting allows it. See <see cref="ConnectionEvents"/>.</summary>
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
            _log.Info("session", $"Could not add to the notification history ({entry.Message}): {ex.Message}");
        }
    }

    /// <summary>Config files hold passwords; remove any left by a crash.</summary>
    private void DeleteLeftoverConfigFiles()
    {
        if (!Directory.Exists(_paths.RuntimeDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_paths.RuntimeDirectory, "*.conf"))
        {
            File.Delete(file);
        }
    }
}
