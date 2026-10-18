using Microsoft.Extensions.Time.Testing;
using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Tests.Fakes;

namespace OpenFortiClient.Core.Tests.Engine;

public sealed class ReconnectSupervisorTests
{
    private readonly FakeEngine _engine = new();
    private readonly FakeTimeProvider _time = new();
    private readonly List<ReconnectSchedule> _scheduled = [];
    private readonly List<string> _paused = [];
    private readonly ReconnectSupervisor _supervisor;
    private int _reconnects;
    private TaskCompletionSource _reconnected = NewSignal();

    public ReconnectSupervisorTests()
    {
        _supervisor = new ReconnectSupervisor(_engine, _ =>
        {
            Interlocked.Increment(ref _reconnects);
            _reconnected.TrySetResult();
            return Task.CompletedTask;
        }, _time);
        _supervisor.Scheduled += (_, s) => _scheduled.Add(s);
        _supervisor.Paused += (_, reason) => _paused.Add(reason);
    }

    [Fact]
    public async Task Reconnects_after_a_lost_tunnel_when_always_up()
    {
        ConnectedWithAlwaysUp();

        _engine.Set(VpnStatus.Error, VpnErrorKind.TunnelFailed);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(_scheduled).Delay);
        Assert.Equal(1, _scheduled[0].Attempt);
        Assert.NotNull(_supervisor.Pending);

        await AdvanceUntilReconnect(TimeSpan.FromSeconds(5));
        Assert.Equal(1, _reconnects);
    }

    [Fact]
    public void Does_nothing_without_always_up()
    {
        _supervisor.Arm(alwaysUp: false);
        _engine.Set(VpnStatus.Connected);

        _engine.Set(VpnStatus.Error, VpnErrorKind.TunnelFailed);

        Assert.Empty(_scheduled);
        Assert.Null(_supervisor.Pending);
    }

    [Fact]
    public async Task Backs_off_5_10_30_then_60_seconds()
    {
        ConnectedWithAlwaysUp();
        _engine.Set(VpnStatus.Error, VpnErrorKind.TunnelFailed);

        foreach (var delay in new[] { 5, 10, 30, 60 })
        {
            await AdvanceUntilReconnect(TimeSpan.FromSeconds(delay));
            FailedAttempt();
        }

        Assert.Equal([5, 10, 30, 60, 60], _scheduled.Select(s => (int)s.Delay.TotalSeconds));
        Assert.Equal([1, 2, 3, 4, 5], _scheduled.Select(s => s.Attempt));
    }

    [Fact]
    public async Task A_successful_reconnect_resets_the_back_off()
    {
        ConnectedWithAlwaysUp();
        _engine.Set(VpnStatus.Error, VpnErrorKind.TunnelFailed);
        await AdvanceUntilReconnect(TimeSpan.FromSeconds(5));
        FailedAttempt();
        await AdvanceUntilReconnect(TimeSpan.FromSeconds(10));
        _engine.Set(VpnStatus.Connecting);
        _engine.Set(VpnStatus.Connected);

        _engine.Set(VpnStatus.Error, VpnErrorKind.TunnelFailed);

        Assert.Equal(TimeSpan.FromSeconds(5), _scheduled[^1].Delay);
        Assert.Equal(1, _scheduled[^1].Attempt);
    }

    [Theory]
    [InlineData(VpnErrorKind.AuthenticationFailed)]
    [InlineData(VpnErrorKind.UntrustedCertificate)]
    [InlineData(VpnErrorKind.OtpRequired)]
    [InlineData(VpnErrorKind.InvalidProfile)]
    [InlineData(VpnErrorKind.EngineNotFound)]
    [InlineData(VpnErrorKind.ElevationFailed)]
    public async Task Pauses_when_a_reconnect_needs_the_user(VpnErrorKind kind)
    {
        ConnectedWithAlwaysUp();
        _engine.Set(VpnStatus.Error, VpnErrorKind.TunnelFailed);
        await AdvanceUntilReconnect(TimeSpan.FromSeconds(5));
        _engine.Set(VpnStatus.Connecting);

        _engine.Set(VpnStatus.Error, kind);

        Assert.Single(_scheduled);
        Assert.Equal($"{kind} happened.", Assert.Single(_paused));
        Assert.Null(_supervisor.Pending);
    }

    [Fact]
    public async Task Stop_cancels_the_waiting_reconnect()
    {
        ConnectedWithAlwaysUp();
        _engine.Set(VpnStatus.Error, VpnErrorKind.TunnelFailed);

        _supervisor.Stop();
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        Assert.Equal(0, _reconnects);
        Assert.Null(_supervisor.Pending);
    }

    [Fact]
    public async Task A_manual_connect_replaces_the_waiting_reconnect()
    {
        ConnectedWithAlwaysUp();
        _engine.Set(VpnStatus.Error, VpnErrorKind.TunnelFailed);

        _supervisor.Arm(alwaysUp: true);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        Assert.Equal(0, _reconnects);
    }

    [Fact]
    public async Task RetryNow_skips_the_wait()
    {
        ConnectedWithAlwaysUp();
        _engine.Set(VpnStatus.Error, VpnErrorKind.TunnelFailed);

        _supervisor.RetryNow();
        await _reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, _reconnects);
    }

    [Fact]
    public void RetryNow_does_nothing_when_nothing_waits()
    {
        ConnectedWithAlwaysUp();

        _supervisor.RetryNow();

        Assert.Equal(0, _reconnects);
    }

    [Fact]
    public void Uses_the_configured_delays()
    {
        _supervisor.Delays = [TimeSpan.FromSeconds(1)];
        ConnectedWithAlwaysUp();

        _engine.Set(VpnStatus.Error, VpnErrorKind.TunnelFailed);

        Assert.Equal(TimeSpan.FromSeconds(1), _scheduled[0].Delay);
    }

    [Fact]
    public async Task A_network_change_during_an_attempt_retries_right_after_it_fails()
    {
        // Review issue 2: RetryNow during a running attempt used to be lost, and the next full wait followed.
        _supervisor.Delays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60)];
        ConnectedWithAlwaysUp();
        _engine.Set(VpnStatus.Error, VpnErrorKind.TunnelFailed);
        await AdvanceUntilReconnect(TimeSpan.FromSeconds(5));
        _engine.Set(VpnStatus.Connecting);

        _supervisor.RetryNow();
        _reconnected = NewSignal();
        _engine.Set(VpnStatus.Error, VpnErrorKind.GatewayUnreachable);

        await _reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, _reconnects);
    }

    [Fact]
    public async Task Stop_cancels_the_running_attempt_outside_its_lock()
    {
        // Review issue 7: Cancel() ran the attempt's cancellation callbacks while the lock was held.
        var callbackRan = NewSignal();
        var lockWasFree = false;
        ReconnectSupervisor? supervisor = null;
        supervisor = new ReconnectSupervisor(_engine, ct =>
        {
            ct.Register(() =>
            {
                var other = Task.Run(() => supervisor!.IsActive);
                lockWasFree = other.Wait(TimeSpan.FromMilliseconds(500));
                callbackRan.TrySetResult();
            });
            return Task.Delay(Timeout.Infinite, ct);
        }, _time);
        supervisor.Arm(alwaysUp: true);
        _engine.Set(VpnStatus.Connecting);
        _engine.Set(VpnStatus.Connected);
        _engine.Set(VpnStatus.Error, VpnErrorKind.TunnelFailed);
        supervisor.RetryNow();
        await WaitUntil(() => supervisor.IsActive && supervisor.Pending is null);

        supervisor.Stop();
        await callbackRan.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(lockWasFree);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }
    }

    private void ConnectedWithAlwaysUp()
    {
        _supervisor.Arm(alwaysUp: true);
        _engine.Set(VpnStatus.Connecting);
        _engine.Set(VpnStatus.Connected);
    }

    /// <summary>The engine's states for a reconnect attempt that fails to reach the gateway.</summary>
    private void FailedAttempt()
    {
        _engine.Set(VpnStatus.Connecting);
        _engine.Set(VpnStatus.Error, VpnErrorKind.GatewayUnreachable);
    }

    private async Task AdvanceUntilReconnect(TimeSpan delay)
    {
        _reconnected = NewSignal();
        _time.Advance(delay);
        await _reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
