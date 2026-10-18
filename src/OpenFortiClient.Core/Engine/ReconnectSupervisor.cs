namespace OpenFortiClient.Core.Engine;

/// <summary>A reconnect that waits: the how-many-th attempt, after which delay, at what time.</summary>
public sealed record ReconnectSchedule(int Attempt, TimeSpan Delay, DateTimeOffset At);

/// <summary>
/// "Always up": reconnects after a lost tunnel, waiting longer after each failed attempt, until it
/// works, the user stops it, or an error needs the user (wrong password, new certificate, …).
/// See docs/architecture.md, "Reconnect (always up)".
/// </summary>
public sealed class ReconnectSupervisor
{
    private readonly IVpnEngine _engine;
    private readonly Func<CancellationToken, Task> _reconnect;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();

    private VpnStatus _last = VpnStatus.Disconnected;
    private bool _armed;
    private bool _reconnecting;
    private int _attempt;
    private CancellationTokenSource? _waiting;
    private TaskCompletionSource? _skip;

    // A network change arrived while an attempt was running: if it fails, retry at once.
    private bool _retryAfterAttempt;

    /// <param name="reconnect">Connects again with the last profile and credentials.</param>
    public ReconnectSupervisor(IVpnEngine engine, Func<CancellationToken, Task> reconnect, TimeProvider? time = null)
    {
        _engine = engine;
        _reconnect = reconnect;
        _time = time ?? TimeProvider.System;
        _engine.StateChanged += OnStateChanged;
    }

    public static IReadOnlyList<TimeSpan> DefaultDelays { get; } =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    /// <summary>Wait before each attempt; the last one repeats.</summary>
    public IReadOnlyList<TimeSpan> Delays { get; set; } = DefaultDelays;

    /// <summary>The reconnect that is waiting, or null.</summary>
    public ReconnectSchedule? Pending { get; private set; }

    /// <summary>Waiting for, or running, a reconnect.</summary>
    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return Pending is not null || _reconnecting;
            }
        }
    }

    /// <summary>A reconnect was scheduled. Raised on the thread that changed the engine state.</summary>
    public event EventHandler<ReconnectSchedule>? Scheduled;

    /// <summary>Reconnecting stopped because of an error the user has to deal with; the argument says which.</summary>
    public event EventHandler<string>? Paused;

    /// <summary>Waiting or reconnecting ended: connected again, stopped, or paused.</summary>
    public event EventHandler? Ended;

    /// <summary>The user connects; reconnect after a drop only if the profile is always up.</summary>
    /// <returns>Whether a reconnect was waiting or running, and is now replaced.</returns>
    public bool Arm(bool alwaysUp)
    {
        var wasActive = Reset(armed: alwaysUp);
        if (wasActive)
        {
            Ended?.Invoke(this, EventArgs.Empty);
        }

        return wasActive;
    }

    /// <summary>The user disconnects or stops reconnecting.</summary>
    /// <returns>Whether a reconnect was waiting or running.</returns>
    public bool Stop()
    {
        var wasActive = Reset(armed: false);
        if (wasActive)
        {
            Ended?.Invoke(this, EventArgs.Empty);
        }

        return wasActive;
    }

    /// <summary>
    /// Skips the wait, for example after a network change. During an attempt, a failure of that attempt
    /// is retried at once instead of after the next delay. Does nothing when nothing waits or runs.
    /// </summary>
    public void RetryNow()
    {
        lock (_lock)
        {
            if (_skip is not null)
            {
                _skip.TrySetResult();
            }
            else if (_reconnecting)
            {
                _retryAfterAttempt = true;
            }
        }
    }

    private void OnStateChanged(object? sender, VpnState state)
    {
        VpnStatus previous;
        bool reconnecting;
        lock (_lock)
        {
            previous = _last;
            _last = state.Status;
            reconnecting = _reconnecting;
        }

        switch (state.Status)
        {
            case VpnStatus.Connected:
                lock (_lock)
                {
                    _attempt = 0;
                    _reconnecting = false;
                }

                if (reconnecting)
                {
                    Ended?.Invoke(this, EventArgs.Empty);
                }

                break;

            // A lost tunnel, or a reconnect attempt that failed.
            case VpnStatus.Error when previous == VpnStatus.Connected || reconnecting:
                bool armed;
                lock (_lock)
                {
                    armed = _armed;
                    _reconnecting = false;
                }

                if (!armed)
                {
                    break;
                }

                if (NeedsTheUser(state.Error?.Kind))
                {
                    Reset(armed: false);
                    Paused?.Invoke(this, state.Error?.Message ?? "The connection needs your attention.");
                    Ended?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    Schedule();
                }

                break;
        }
    }

    private static bool NeedsTheUser(VpnErrorKind? kind) => kind is
        VpnErrorKind.AuthenticationFailed or VpnErrorKind.UntrustedCertificate or VpnErrorKind.OtpRequired
        or VpnErrorKind.InvalidProfile or VpnErrorKind.EngineNotFound or VpnErrorKind.ElevationFailed;

    private void Schedule()
    {
        ReconnectSchedule schedule;
        CancellationTokenSource waiting;
        CancellationTokenSource? previous;
        TaskCompletionSource skip;
        lock (_lock)
        {
            var delay = _retryAfterAttempt ? TimeSpan.Zero
                : Delays.Count == 0 ? DefaultDelays[0]
                : Delays[Math.Min(_attempt, Delays.Count - 1)];
            _retryAfterAttempt = false;
            _attempt++;
            previous = _waiting;
            waiting = _waiting = new CancellationTokenSource();
            skip = _skip = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            schedule = Pending = new ReconnectSchedule(_attempt, delay, _time.GetUtcNow() + delay);
        }

        CancelOutsideLock(previous);

        Scheduled?.Invoke(this, schedule);
        _ = WaitAndReconnectAsync(schedule.Delay, skip.Task, waiting);
    }

    private async Task WaitAndReconnectAsync(TimeSpan delay, Task skip, CancellationTokenSource waiting)
    {
        var ct = waiting.Token;
        try
        {
            await Task.WhenAny(Task.Delay(delay, _time, ct), skip);
            lock (_lock)
            {
                ct.ThrowIfCancellationRequested();
                Pending = null;
                _skip = null;
                _reconnecting = true;
            }

            await _reconnect(ct);
        }
        catch (OperationCanceledException)
        {
            // Stopped, or replaced by a manual connect.
        }
        catch (Exception)
        {
            // The reconnect could not even start (for example the engine was busy): try again later.
            bool armed;
            lock (_lock)
            {
                _reconnecting = false;
                armed = _armed && !ct.IsCancellationRequested;
            }

            if (armed)
            {
                Schedule();
            }
        }
    }

    /// <returns>Whether a reconnect was waiting or running.</returns>
    private bool Reset(bool armed)
    {
        CancellationTokenSource? waiting;
        bool wasActive;
        lock (_lock)
        {
            wasActive = Pending is not null || _reconnecting;
            _armed = armed;
            _reconnecting = false;
            _retryAfterAttempt = false;
            _attempt = 0;
            Pending = null;
            _skip = null;
            waiting = _waiting;
            _waiting = null;
        }

        CancelOutsideLock(waiting);
        return wasActive;
    }

    /// <summary>
    /// Cancel runs the token's callbacks inline, including the engine's cancellation of a running attempt,
    /// which raises events back into this class. Never do that while holding the lock.
    /// </summary>
    private static void CancelOutsideLock(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already finished.
        }
    }
}
