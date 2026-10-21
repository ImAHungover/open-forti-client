using Microsoft.Extensions.Time.Testing;
using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Tests.Fakes;

namespace OpenFortiClient.Core.Tests.Engine;

public sealed class SystemVpnConnectionTests
{
    private const int PeerDead = 9; // pppd's PPP_ERR_PEERDEAD: the server stopped answering.

    private readonly FakeSystemVpns _vpns = new();
    private readonly SystemVpnConnection _connection;
    private readonly List<VpnState> _states = [];

    public SystemVpnConnectionTests()
    {
        _connection = new SystemVpnConnection(_vpns, FakeSystemVpns.Vpn);
        _vpns.Changed += (_, change) => _connection.OnChanged(change.State);
        _connection.StateChanged += (_, state) => _states.Add(state);
    }

    [Fact]
    public async Task Switching_on_starts_the_service_and_follows_its_states()
    {
        await _connection.ConnectAsync();
        _vpns.Set(SystemVpnStatus.Connecting);
        _vpns.Set(SystemVpnStatus.Connected);

        Assert.Equal([$"start {FakeSystemVpns.Vpn.Id}"], _vpns.Calls);
        Assert.Equal([VpnStatus.Connecting, VpnStatus.Connected], _states.Select(s => s.Status));
    }

    [Fact]
    public async Task Switching_off_stops_it_and_ends_disconnected()
    {
        await On();

        await _connection.DisconnectAsync();
        _vpns.Set(SystemVpnStatus.Disconnecting);
        _vpns.Set(SystemVpnStatus.Disconnected);

        Assert.Equal($"stop {FakeSystemVpns.Vpn.Id}", _vpns.Calls[^1]);
        Assert.Equal([VpnStatus.Disconnecting, VpnStatus.Disconnected], _states.Select(s => s.Status));
    }

    [Fact]
    public async Task A_drop_while_on_is_a_lost_connection()
    {
        await On();

        // macOS passes through Disconnecting; it must not hide that the connection was up.
        _vpns.Set(SystemVpnStatus.Disconnecting);
        _vpns.Set(SystemVpnStatus.Disconnected, PeerDead);

        var state = Assert.Single(_states);
        Assert.Equal(VpnStatus.Error, state.Status);
        Assert.Equal(VpnErrorKind.TunnelFailed, state.Error?.Kind);
        Assert.Equal("VPN lost its connection.", state.Error?.Message);
    }

    [Fact]
    public async Task A_switch_off_in_System_Settings_is_respected()
    {
        await On();

        // What macOS reported on the real L2TP VPN for a switch-off in System Settings (2026-10-21).
        _vpns.Set(SystemVpnStatus.Disconnecting, lastCause: 0);
        _vpns.Set(SystemVpnStatus.Disconnected, SystemVpnCause.SwitchedOff);

        Assert.Equal(VpnStatus.Disconnected, Assert.Single(_states).Status);
    }

    [Fact]
    public async Task A_failed_attempt_is_reported_as_unreachable()
    {
        await _connection.ConnectAsync();
        _vpns.Set(SystemVpnStatus.Connecting);
        _states.Clear();

        _vpns.Set(SystemVpnStatus.Disconnected, PeerDead);

        var error = Assert.Single(_states).Error;
        Assert.Equal(VpnErrorKind.GatewayUnreachable, error?.Kind);
        Assert.Equal("VPN could not connect. Check its server and login in System Settings.", error?.Message);
    }

    [Fact]
    public async Task macOS_refusing_to_start_it_says_what_to_do()
    {
        _vpns.StartError = "No service.";

        await _connection.ConnectAsync();

        var error = Assert.Single(_states).Error;
        Assert.Equal(VpnErrorKind.InvalidProfile, error?.Kind);
        Assert.Equal("macOS could not start VPN: No service. Connect it once in System Settings and save its password.", error?.Message);
    }

    [Fact]
    public async Task Switching_off_after_a_failed_start_clears_the_error()
    {
        _vpns.StartError = "No service.";
        await _connection.ConnectAsync();

        await _connection.DisconnectAsync();

        Assert.Equal(VpnStatus.Disconnected, _states[^1].Status);
        Assert.DoesNotContain(_vpns.Calls, c => c.StartsWith("stop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Switching_on_when_already_up_does_not_start_it_again()
    {
        await On();

        await _connection.ConnectAsync();

        Assert.Single(_vpns.Calls);
    }

    [Fact]
    public void A_service_connected_in_System_Settings_counts_as_on()
    {
        _vpns.Set(SystemVpnStatus.Connecting);
        _vpns.Set(SystemVpnStatus.Connected);
        _states.Clear();

        _vpns.Set(SystemVpnStatus.Disconnected, PeerDead);

        Assert.Equal(VpnErrorKind.TunnelFailed, Assert.Single(_states).Error?.Kind);
    }

    [Fact]
    public async Task Keep_it_up_reconnects_after_a_drop()
    {
        var time = new FakeTimeProvider();
        var reconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var supervisor = new ReconnectSupervisor(_connection, async ct =>
        {
            await _connection.ConnectAsync(ct);
            reconnected.TrySetResult();
        }, time);
        supervisor.Arm(alwaysUp: true);
        await On();

        _vpns.Set(SystemVpnStatus.Disconnected, PeerDead);
        Assert.NotNull(supervisor.Pending);
        time.Advance(TimeSpan.FromSeconds(5));
        await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, _vpns.Calls.Count(c => c.StartsWith("start", StringComparison.Ordinal)));
    }

    [Fact]
    public void Keep_it_up_reconnects_a_VPN_that_was_already_up_when_the_app_started()
    {
        _vpns.Set(SystemVpnStatus.Connected);
        var connection = new SystemVpnConnection(_vpns, FakeSystemVpns.Vpn);
        _vpns.Changed += (_, change) => connection.OnChanged(change.State);
        var supervisor = new ReconnectSupervisor(connection, connection.ConnectAsync, new FakeTimeProvider());
        supervisor.Arm(alwaysUp: true);

        _vpns.Set(SystemVpnStatus.Disconnected, PeerDead);

        Assert.NotNull(supervisor.Pending);
    }

    [Fact]
    public async Task Keep_it_up_ignores_a_switch_off_and_pauses_when_macOS_will_not_start_it()
    {
        var time = new FakeTimeProvider();
        var paused = new List<string>();
        var supervisor = new ReconnectSupervisor(_connection, _connection.ConnectAsync, time);
        supervisor.Paused += (_, reason) => paused.Add(reason);
        supervisor.Arm(alwaysUp: true);

        await On();
        _vpns.Set(SystemVpnStatus.Disconnected, SystemVpnCause.SwitchedOff);
        Assert.Null(supervisor.Pending);

        await On();
        _vpns.StartError = "No service.";
        _vpns.Set(SystemVpnStatus.Disconnected, PeerDead);
        time.Advance(TimeSpan.FromSeconds(5));
        await WaitUntil(() => paused.Count > 0);

        Assert.Null(supervisor.Pending);
        Assert.StartsWith("macOS could not start VPN: No service.", Assert.Single(paused), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, SystemVpnDisconnect.Stopped)]
    [InlineData(0, SystemVpnDisconnect.Stopped)]
    [InlineData(5, SystemVpnDisconnect.Stopped)]
    [InlineData(9, SystemVpnDisconnect.Dropped)]
    [InlineData(11, SystemVpnDisconnect.Dropped)]
    public void Reads_the_cause_macOS_reports(int? lastCause, SystemVpnDisconnect expected) =>
        Assert.Equal(expected, SystemVpnCause.Classify(lastCause));

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }
    }

    private async Task On()
    {
        await _connection.ConnectAsync();
        _vpns.Set(SystemVpnStatus.Connecting);
        _vpns.Set(SystemVpnStatus.Connected);
        _states.Clear();
    }
}
