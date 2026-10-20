using OpenFortiClient.Core.Logging;
using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.Core.Engine.OpenFortiVpn;

/// <summary>
/// <see cref="IVpnEngine"/> on top of the openfortivpn command-line client.
/// See docs/architecture.md, "VPN engine".
/// </summary>
public sealed class OpenFortiVpnEngine(IPrivilegedLauncher launcher, OpenFortiVpnEngineOptions options) : IVpnEngine
{
    private readonly Lock _lock = new();
    private VpnState _state = VpnState.Disconnected;
    private IPrivilegedProcess? _process;
    private Task? _monitor;
    private CancellationTokenSource? _attempt;
    private bool _disconnectRequested;

    public event EventHandler<VpnState>? StateChanged;

    public event EventHandler<string>? LogLine;

    public event EventHandler<GatewayFailure>? GatewayFailed;

    /// <summary>Updated by the app when settings change; used from the next connect on.</summary>
    public OpenFortiVpnEngineOptions Options { get; set; } = options;

    public VpnState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public async Task ConnectAsync(VpnProfile profile, VpnCredentials credentials, CancellationToken ct = default)
    {
        Task? previous;
        IPrivilegedProcess? previousProcess;
        CancellationTokenSource attempt;
        lock (_lock)
        {
            if (_state.Status is not (VpnStatus.Disconnected or VpnStatus.Error) || _attempt is not null)
            {
                throw new InvalidOperationException($"Cannot connect while {_state.Status}.");
            }

            previous = _monitor;
            previousProcess = _process;
            _disconnectRequested = false;

            // Cancelled by DisconnectAsync, so a disconnect works at any point of the attempt,
            // including between two gateways when no openfortivpn is running.
            attempt = _attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }

        try
        {
            await ConnectCoreAsync(profile, credentials, previous, previousProcess, Options, attempt.Token);
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested)
        {
            SetState(VpnState.Disconnected);
            if (ct.IsCancellationRequested)
            {
                throw;
            }
        }
        finally
        {
            lock (_lock)
            {
                if (_attempt == attempt)
                {
                    _attempt = null;
                }
            }

            attempt.Dispose();
        }
    }

    public async Task DisconnectAsync()
    {
        IPrivilegedProcess? process;
        Task? monitor;
        CancellationTokenSource? attempt;
        lock (_lock)
        {
            process = _process;
            monitor = _monitor;
            attempt = _attempt;
            if (process is null && attempt is null)
            {
                return;
            }

            _disconnectRequested = true;
        }

        SetState(State with { Status = VpnStatus.Disconnecting, Error = null });
        Log("Disconnecting");
        try
        {
            attempt?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The attempt ended in the meantime.
        }

        // No openfortivpn yet (or between two gateways): ConnectAsync sees the cancellation and sets the state.
        if (process is null)
        {
            return;
        }

        if (!await StopAsync(process))
        {
            Fail(new VpnError(
                VpnErrorKind.TunnelFailed,
                $"openfortivpn did not stop within {2 * Options.DisconnectTimeout.TotalSeconds:0} seconds. It may still be removing its routes and DNS settings; try again in a moment."));
            return;
        }

        // While connected, the monitor sets the final state. While connecting, ConnectAsync does.
        if (monitor is not null)
        {
            await monitor;
        }
    }

    private async Task ConnectCoreAsync(
        VpnProfile profile, VpnCredentials credentials, Task? previous, IPrivilegedProcess? previousProcess, OpenFortiVpnEngineOptions opts, CancellationToken ct)
    {
        // After a lost tunnel openfortivpn can still be running, trying to log out from a gateway it cannot
        // reach. A second one must not start before it is gone, so wait, with feedback and a limit.
        if (previous is { IsCompleted: false })
        {
            SetState(new VpnState(VpnStatus.Connecting, profile.Gateways.FirstOrDefault()));
            Log("Waiting for the previous openfortivpn to stop");
            previousProcess?.Terminate();

            var limit = 2 * opts.DisconnectTimeout;
            if (await Task.WhenAny(previous, Task.Delay(limit, ct)) != previous)
            {
                ct.ThrowIfCancellationRequested();
                Fail(new VpnError(
                    VpnErrorKind.TunnelFailed,
                    $"The previous connection is still closing (openfortivpn did not stop within {limit.TotalSeconds:0} seconds). Try again in a moment."));
                return;
            }
        }

        // The certificate files are checked again here: they can move or disappear after the profile was saved.
        var problems = profile.Validate().Concat(ProfileFiles.Check(profile).Errors).ToList();
        if (profile.ClientKeyInKeychain && string.IsNullOrEmpty(credentials.ClientKeyPem))
        {
            problems.Add("The client key is not in the Keychain. Edit the connection and choose the key file again.");
        }

        if (problems.Count > 0)
        {
            Fail(new VpnError(VpnErrorKind.InvalidProfile, string.Join(" ", problems)));
            return;
        }

        if (!File.Exists(opts.ExecutablePath))
        {
            Fail(new VpnError(
                VpnErrorKind.EngineNotFound,
                opts.ExecutablePath.StartsWith("/opt/homebrew/", StringComparison.Ordinal) || opts.ExecutablePath.StartsWith("/usr/local/", StringComparison.Ordinal)
                    ? $"openfortivpn was not found at {opts.ExecutablePath}. Install it with: brew install openfortivpn"
                    : $"openfortivpn was not found at {opts.ExecutablePath}. Reinstall OpenFortiClient."));
            return;
        }

        VpnError? lastError = null;
        for (var i = 0; i < profile.Gateways.Count; i++)
        {
            var gateway = profile.Gateways[i];
            ct.ThrowIfCancellationRequested();
            SetState(new VpnState(VpnStatus.Connecting, gateway));
            Log($"Connecting to {gateway.Host}:{gateway.Port}");
            ct.ThrowIfCancellationRequested();

            lastError = await TryGatewayAsync(profile, gateway, credentials, opts, ct);
            if (lastError is null)
            {
                return;
            }

            if (ct.IsCancellationRequested || DisconnectRequested)
            {
                SetState(VpnState.Disconnected);
                return;
            }

            Log($"{gateway.Host}: {lastError.Message}");
            if (!IsWorthTryingNextGateway(lastError.Kind))
            {
                break;
            }

            GatewayFailed?.Invoke(this, new GatewayFailure(gateway, lastError, i + 1 < profile.Gateways.Count ? profile.Gateways[i + 1] : null));
        }

        Fail(lastError ?? new VpnError(VpnErrorKind.Unknown, "The connection failed."));
    }

    /// <summary>
    /// SIGTERM, and once more if openfortivpn still runs after the timeout: a second signal interrupts a
    /// logout that is stuck on an unreachable gateway. Never SIGKILL, which would leave routes and DNS behind.
    /// </summary>
    /// <returns>Whether it exited.</returns>
    private async Task<bool> StopAsync(IPrivilegedProcess process)
    {
        for (var signal = 0; signal < 2; signal++)
        {
            process.Terminate();
            if (await Task.WhenAny(process.Exited, Task.Delay(Options.DisconnectTimeout)) == process.Exited)
            {
                return true;
            }
        }

        return false;
    }

    /// <returns>null when the tunnel is up, otherwise why this gateway failed.</returns>
    private async Task<VpnError?> TryGatewayAsync(
        VpnProfile profile, Uri gateway, VpnCredentials credentials, OpenFortiVpnEngineOptions opts, CancellationToken ct)
    {
        var request = new LaunchRequest(
            opts.ExecutablePath,
            OpenFortiVpnConfig.Create(profile, gateway, credentials, opts).Render(),
            OpenFortiVpnOutputParser.PromptMarkers)
        {
            ClientKeyPem = credentials.ClientKeyPem,
        };

        IPrivilegedProcess process;
        try
        {
            process = launcher.Start(request);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new VpnError(VpnErrorKind.ElevationFailed, $"Could not start openfortivpn: {ex.Message}");
        }

        lock (_lock)
        {
            _process = process;
        }

        var connected = false;
        var answered = false;
        using var timeout = new CancellationTokenSource(opts.GatewayTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            VpnError? error = null;
            string? digest = null;

            await foreach (var line in process.Output.ReadAllAsync(linked.Token))
            {
                Log(line);
                if (OpenFortiVpnOutputParser.Parse(line) is not { } ev)
                {
                    continue;
                }

                switch (ev.Kind)
                {
                    case OutputEventKind.ConnectedToGateway:
                        // openfortivpn has no timeout of its own for the steps after this; a dead
                        // route can leave it waiting for minutes.
                        answered = true;
                        timeout.CancelAfter(opts.TunnelSetupTimeout);
                        SetState(new VpnState(VpnStatus.Authenticating, gateway));
                        break;

                    case OutputEventKind.TunnelUp:
                        connected = true;
                        var monitorDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        lock (_lock)
                        {
                            _monitor = monitorDone.Task;
                        }

                        SetState(new VpnState(VpnStatus.Connected, gateway));

                        // Started outside the lock: it raises events, and may finish at once.
                        _ = MonitorAsync(process, gateway, monitorDone);
                        return null;

                    case OutputEventKind.CertificateUntrusted:
                        error = new VpnError(
                            VpnErrorKind.UntrustedCertificate,
                            $"The certificate of {gateway.Host} is not trusted.");
                        break;

                    case OutputEventKind.TrustedCertDigest:
                        digest = ev.Value;
                        break;

                    case OutputEventKind.OtpPrompt:
                        timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                        var otp = credentials.OtpProvider is null ? null : await credentials.OtpProvider(ct);
                        if (string.IsNullOrWhiteSpace(otp))
                        {
                            error = new VpnError(VpnErrorKind.OtpRequired, "The gateway asked for a one-time password.");
                            process.Terminate();
                        }
                        else
                        {
                            // The user may have disconnected, or openfortivpn given up, while the dialog was open.
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                await process.WriteLineAsync(otp.Trim(), ct);
                                timeout.CancelAfter(opts.TunnelSetupTimeout);
                            }
                            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                            {
                                error ??= new VpnError(VpnErrorKind.TunnelFailed, "openfortivpn stopped before the one-time password could be sent.");
                            }
                        }

                        break;

                    case OutputEventKind.PasswordPrompt:
                        error = new VpnError(VpnErrorKind.AuthenticationFailed, "A password is required.");
                        process.Terminate();
                        break;

                    case OutputEventKind.AuthenticationFailed:
                        error = new VpnError(VpnErrorKind.AuthenticationFailed, "Wrong username or password, or the gateway rejected the login.");
                        break;

                    case OutputEventKind.GatewayUnreachable:
                        error ??= new VpnError(VpnErrorKind.GatewayUnreachable, $"Could not reach {gateway.Host} ({ev.Value}).");
                        break;

                    case OutputEventKind.TunnelSetupFailed:
                        // Often follows a "connect:" error; this says more about what failed.
                        if (error is null or { Kind: VpnErrorKind.GatewayUnreachable })
                        {
                            error = new VpnError(
                                VpnErrorKind.TunnelFailed,
                                $"{gateway.Host} accepted the login, but the tunnel could not be set up ({ev.Value}).");
                        }

                        break;

                    case OutputEventKind.TunnelClosed:
                        // This attempt is over, but openfortivpn may keep running to log out from a
                        // gateway it can no longer reach. Stop it instead of waiting.
                        error ??= new VpnError(VpnErrorKind.TunnelFailed, $"The connection to {gateway.Host} closed before the tunnel was up.");
                        process.Terminate();
                        break;

                    case OutputEventKind.ElevationFailed:
                        error = new VpnError(
                            VpnErrorKind.ElevationFailed,
                            "openfortivpn could not run as root. Check the sudoers rule described in the README.");
                        break;
                }
            }

            // Output ended before the tunnel came up: the process has exited.
            var exitCode = await process.Exited;
            error ??= new VpnError(VpnErrorKind.TunnelFailed, $"openfortivpn stopped with exit code {exitCode}.");
            return error.Kind == VpnErrorKind.UntrustedCertificate ? error with { CertificateDigest = digest } : error;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return answered
                ? new VpnError(
                    VpnErrorKind.TunnelFailed,
                    $"{gateway.Host} answered, but the tunnel was not up within {opts.TunnelSetupTimeout.TotalSeconds:0} seconds.")
                : new VpnError(
                    VpnErrorKind.GatewayUnreachable,
                    $"No answer from {gateway.Host} within {opts.GatewayTimeout.TotalSeconds:0} seconds.");
        }
        finally
        {
            if (!connected)
            {
                lock (_lock)
                {
                    _process = null;
                }

                await process.DisposeAsync();
            }
        }
    }

    /// <summary>Follows a running tunnel until openfortivpn exits, then completes <paramref name="done"/>.</summary>
    private async Task MonitorAsync(IPrivilegedProcess process, Uri gateway, TaskCompletionSource done)
    {
        try
        {
            await FollowAsync(process, gateway, done.Task);
        }
        finally
        {
            done.TrySetResult();
        }
    }

    private async Task FollowAsync(IPrivilegedProcess process, Uri gateway, Task self)
    {
        int exitCode;
        var lost = false;
        try
        {
            await foreach (var line in process.Output.ReadAllAsync())
            {
                Log(line);

                // Report a dropped tunnel straight away. openfortivpn itself can take a long time to
                // exit, because it tries to log out from a gateway it can no longer reach.
                if (!lost
                    && OpenFortiVpnOutputParser.Parse(line) is { Kind: OutputEventKind.TunnelClosed }
                    && !DisconnectRequested)
                {
                    lost = true;
                    process.Terminate();
                    Fail(new VpnError(VpnErrorKind.TunnelFailed, $"The connection to {gateway.Host} was lost."));
                }
            }

            exitCode = await process.Exited;
        }
        catch (Exception ex)
        {
            Log($"Lost track of openfortivpn: {ex.Message}");
            exitCode = -1;
        }

        bool requested;
        lock (_lock)
        {
            requested = _disconnectRequested;
            if (_process == process)
            {
                _process = null;
            }

            if (_monitor == self)
            {
                _monitor = null;
            }
        }

        await process.DisposeAsync();

        if (requested)
        {
            Log("Disconnected");
            SetState(VpnState.Disconnected);
        }
        else if (!lost)
        {
            Fail(new VpnError(
                VpnErrorKind.TunnelFailed,
                $"The tunnel to {gateway.Host} closed unexpectedly (exit code {exitCode})."));
        }
    }

    private bool DisconnectRequested
    {
        get
        {
            lock (_lock)
            {
                return _disconnectRequested;
            }
        }
    }

    private static bool IsWorthTryingNextGateway(VpnErrorKind kind) =>
        kind is VpnErrorKind.GatewayUnreachable or VpnErrorKind.TunnelFailed;

    private void Fail(VpnError error) => SetState(new VpnState(VpnStatus.Error, null, error));

    private void SetState(VpnState state)
    {
        lock (_lock)
        {
            _state = state;
        }

        StateChanged?.Invoke(this, state);
    }

    private void Log(string line) => LogLine?.Invoke(this, LogSanitizer.Sanitize(line));
}
