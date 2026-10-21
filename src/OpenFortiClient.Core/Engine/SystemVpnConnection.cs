namespace OpenFortiClient.Core.Engine;

/// <summary>
/// One macOS VPN service as a connection the app switches on and off. Its <see cref="State"/> is
/// reported like the engine's, so <see cref="ReconnectSupervisor"/> can keep it up: a drop while it
/// should be on is an Error after Connected, macOS refusing to start it is an error that needs the user, and a
/// switch-off in System Settings is respected (Disconnected, not an error).
/// </summary>
public sealed class SystemVpnConnection : IConnectionState
{
    private readonly ISystemVpns _vpns;
    private readonly Lock _lock = new();
    private SystemVpnState _current;
    private VpnState _state;

    // Should be on: switched on in the app, by keep it up, or connected from System Settings.
    private bool _wantsUp;

    public SystemVpnConnection(ISystemVpns vpns, SystemVpnService service)
    {
        _vpns = vpns;
        Service = service;
        _current = vpns.State(service.Id);
        _wantsUp = _current.Status is SystemVpnStatus.Connecting or SystemVpnStatus.Connected;
        _state = new VpnState(_current.Status switch
        {
            SystemVpnStatus.Connected => VpnStatus.Connected,
            SystemVpnStatus.Connecting => VpnStatus.Connecting,
            SystemVpnStatus.Disconnecting => VpnStatus.Disconnecting,
            _ => VpnStatus.Disconnected,
        });
    }

    public SystemVpnService Service { get; }

    /// <summary>What macOS last reported.</summary>
    public SystemVpnState Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

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

    public event EventHandler<VpnState>? StateChanged;

    /// <summary>Switches the service on. Does nothing if it is on or coming up.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            _wantsUp = true;
            if (_current.Status is SystemVpnStatus.Connecting or SystemVpnStatus.Connected)
            {
                return;
            }
        }

        ct.ThrowIfCancellationRequested();
        if (await _vpns.StartAsync(Service.Id) is { } error)
        {
            // macOS would not even try, usually because the service is missing settings or credentials.
            Set(new VpnState(VpnStatus.Error, Error: new VpnError(
                VpnErrorKind.InvalidProfile,
                $"macOS could not start {Service.Name}: {error.TrimEnd('.')}. Connect it once in System Settings and save its password.")));
        }
    }

    /// <summary>Switches the service off.</summary>
    public async Task DisconnectAsync()
    {
        bool down;
        bool clearError;
        lock (_lock)
        {
            _wantsUp = false;
            down = _current.Status == SystemVpnStatus.Disconnected;

            // Switching off after a failed attempt clears its error.
            clearError = down && _state.Status == VpnStatus.Error;
            if (clearError)
            {
                _state = VpnState.Disconnected;
            }
        }

        if (clearError)
        {
            StateChanged?.Invoke(this, VpnState.Disconnected);
        }

        if (down)
        {
            return;
        }

        if (await _vpns.StopAsync(Service.Id) is { } error)
        {
            Set(new VpnState(VpnStatus.Error, Error: new VpnError(VpnErrorKind.Unknown, $"macOS could not stop {Service.Name}: {error.TrimEnd('.')}.")));
        }
    }

    /// <summary>macOS reported a new state for this service.</summary>
    public void OnChanged(SystemVpnState next)
    {
        VpnState? changed;
        lock (_lock)
        {
            var previous = _current.Status;
            _current = next;
            changed = next.Status switch
            {
                SystemVpnStatus.Connected => Connected(),
                SystemVpnStatus.Connecting => new VpnState(VpnStatus.Connecting),

                // While it should stay up, going down is reported once it is down, as a drop: the
                // supervisor reconnects only after Error straight from Connected.
                SystemVpnStatus.Disconnecting => _wantsUp ? null : new VpnState(VpnStatus.Disconnecting),
                _ => Disconnected(previous, next.LastCause),
            };

            if (changed is null || changed == _state)
            {
                return;
            }

            _state = changed;
        }

        StateChanged?.Invoke(this, changed);
    }

    private VpnState Connected()
    {
        _wantsUp = true;
        return new VpnState(VpnStatus.Connected);
    }

    private VpnState Disconnected(SystemVpnStatus previous, int? lastCause)
    {
        if (!_wantsUp || previous == SystemVpnStatus.Disconnected)
        {
            return VpnState.Disconnected;
        }

        if (SystemVpnCause.Classify(lastCause) == SystemVpnDisconnect.Stopped)
        {
            _wantsUp = false;
            return VpnState.Disconnected;
        }

        return previous is SystemVpnStatus.Connected or SystemVpnStatus.Disconnecting
            ? new VpnState(VpnStatus.Error, Error: new VpnError(VpnErrorKind.TunnelFailed, $"{Service.Name} lost its connection."))
            : new VpnState(VpnStatus.Error, Error: new VpnError(
                VpnErrorKind.GatewayUnreachable, $"{Service.Name} could not connect. Check its server and login in System Settings."));
    }

    private void Set(VpnState state)
    {
        lock (_lock)
        {
            if (state == _state)
            {
                return;
            }

            _state = state;
        }

        StateChanged?.Invoke(this, state);
    }
}
