using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Notifications;

namespace OpenFortiClient.Core.Tests.Notifications;

public sealed class ConnectionEventsTests
{
    private static readonly Uri Vpn1 = new("https://vpn1.example.com:443/");
    private static readonly Uri Vpn2 = new("https://vpn2.example.com:443/");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 15, 0, 0, TimeSpan.FromHours(2));

    private readonly ConnectionEvents _events = new();

    [Fact]
    public void Connected_is_recorded_with_a_banner()
    {
        _events.OnStateChanged(new VpnState(VpnStatus.Connecting, Vpn1), Now);

        var e = _events.OnStateChanged(new VpnState(VpnStatus.Connected, Vpn1), Now);

        Assert.Equal(new NotificationEntry(Now, "VPN", "Connected to vpn1.example.com."), e?.Entry);
        Assert.Equal(new Banner("VPN connected", "Connected to vpn1.example.com.", NotificationSeverity.Info), e?.Banner);
    }

    [Fact]
    public void A_disconnect_after_connecting_is_recorded_without_a_banner()
    {
        Run(VpnStatus.Connecting, VpnStatus.Connected, VpnStatus.Disconnecting);

        var e = _events.OnStateChanged(VpnState.Disconnected, Now);

        Assert.Equal("Disconnected.", e?.Entry.Message);
        Assert.Null(e?.Banner);
    }

    [Fact]
    public void A_cancelled_attempt_is_recorded_as_cancelled()
    {
        Run(VpnStatus.Connecting, VpnStatus.Disconnecting);

        var e = _events.OnStateChanged(VpnState.Disconnected, Now);

        Assert.Equal("Connection attempt cancelled.", e?.Entry.Message);
        Assert.Null(e?.Banner);
    }

    [Fact]
    public void An_error_after_connecting_is_a_lost_connection()
    {
        Run(VpnStatus.Connecting, VpnStatus.Connected);

        var e = _events.OnStateChanged(Error(VpnErrorKind.TunnelFailed, "The connection to vpn1.example.com was lost."), Now);

        Assert.Equal(NotificationSeverity.Error, e?.Entry.Severity);
        Assert.Equal(new Banner("VPN connection lost", "The connection to vpn1.example.com was lost.", NotificationSeverity.Error), e?.Banner);
    }

    [Fact]
    public void An_error_before_connecting_is_a_failed_connection()
    {
        Run(VpnStatus.Connecting);

        var e = _events.OnStateChanged(Error(VpnErrorKind.GatewayUnreachable, "Could not reach vpn1.example.com."), Now);

        Assert.Equal("VPN connection failed", e?.Banner?.Title);
    }

    [Fact]
    public void A_new_attempt_after_a_lost_connection_starts_fresh()
    {
        Run(VpnStatus.Connecting, VpnStatus.Connected);
        _events.OnStateChanged(Error(VpnErrorKind.TunnelFailed, "lost"), Now);
        Run(VpnStatus.Connecting);

        var e = _events.OnStateChanged(Error(VpnErrorKind.GatewayUnreachable, "Could not reach vpn1.example.com."), Now);

        Assert.Equal("VPN connection failed", e?.Banner?.Title);
    }

    [Fact]
    public void A_failure_right_after_a_lost_connection_is_a_failed_connection()
    {
        // Review issue 6: an invalid profile fails without passing through Connecting.
        Run(VpnStatus.Connecting, VpnStatus.Connected);
        _events.OnStateChanged(Error(VpnErrorKind.TunnelFailed, "lost"), Now);

        var e = _events.OnStateChanged(Error(VpnErrorKind.InvalidProfile, "Client key file not found."), Now);

        Assert.Equal("VPN connection failed", e?.Banner?.Title);
    }

    [Fact]
    public void An_untrusted_certificate_is_recorded_without_a_banner_because_the_app_asks()
    {
        Run(VpnStatus.Connecting);

        var e = _events.OnStateChanged(Error(VpnErrorKind.UntrustedCertificate, "The certificate of vpn1.example.com is not trusted."), Now);

        Assert.NotNull(e?.Entry);
        Assert.Null(e?.Banner);
    }

    [Fact]
    public void Intermediate_states_record_nothing()
    {
        Assert.Null(_events.OnStateChanged(new VpnState(VpnStatus.Connecting, Vpn1), Now));
        Assert.Null(_events.OnStateChanged(new VpnState(VpnStatus.Authenticating, Vpn1), Now));
    }

    [Fact]
    public void A_failover_is_a_warning_with_a_banner()
    {
        var failure = new GatewayFailure(Vpn1, new VpnError(VpnErrorKind.GatewayUnreachable, "Could not reach vpn1.example.com (connect: Connection refused)."), Vpn2);

        var e = ConnectionEvents.OnGatewayFailed(failure, Now);

        Assert.Equal(NotificationSeverity.Warning, e?.Entry.Severity);
        Assert.Equal("vpn1.example.com failed (Could not reach vpn1.example.com (connect: Connection refused)). Trying vpn2.example.com.", e?.Entry.Message);
        Assert.Equal("Trying another gateway", e?.Banner?.Title);
    }

    [Fact]
    public void The_last_gateway_failing_adds_nothing_because_the_error_state_follows()
    {
        var failure = new GatewayFailure(Vpn2, new VpnError(VpnErrorKind.GatewayUnreachable, "Could not reach vpn2.example.com."), null);

        Assert.Null(ConnectionEvents.OnGatewayFailed(failure, Now));
    }

    [Fact]
    public void A_scheduled_reconnect_is_recorded_without_a_banner()
    {
        var e = _events.OnReconnectScheduled(new ReconnectSchedule(3, TimeSpan.FromSeconds(30), Now), "vpn1.example.com", Now);

        Assert.Equal("Reconnecting to vpn1.example.com in 30 s (attempt 3).", e.Entry.Message);
        Assert.Null(e.Banner);
    }

    [Fact]
    public void Failed_attempts_while_reconnecting_get_no_banner()
    {
        Run(VpnStatus.Connecting, VpnStatus.Connected);
        _events.OnStateChanged(Error(VpnErrorKind.TunnelFailed, "lost"), Now);
        _events.OnReconnectScheduled(new ReconnectSchedule(1, TimeSpan.FromSeconds(5), Now), "vpn1.example.com", Now);
        Run(VpnStatus.Connecting);

        var e = _events.OnStateChanged(Error(VpnErrorKind.GatewayUnreachable, "Could not reach vpn1.example.com."), Now);

        Assert.NotNull(e?.Entry);
        Assert.Null(e?.Banner);
    }

    [Fact]
    public void A_successful_reconnect_says_reconnected()
    {
        Run(VpnStatus.Connecting, VpnStatus.Connected);
        _events.OnStateChanged(Error(VpnErrorKind.TunnelFailed, "lost"), Now);
        _events.OnReconnectScheduled(new ReconnectSchedule(1, TimeSpan.FromSeconds(5), Now), "vpn1.example.com", Now);
        Run(VpnStatus.Connecting);

        var e = _events.OnStateChanged(new VpnState(VpnStatus.Connected, Vpn1), Now);

        Assert.Equal("Reconnected to vpn1.example.com.", e?.Entry.Message);
        Assert.Equal("VPN reconnected", e?.Banner?.Title);
    }

    [Fact]
    public void A_later_manual_connect_is_a_plain_connect_again()
    {
        _events.OnReconnectScheduled(new ReconnectSchedule(1, TimeSpan.FromSeconds(5), Now), "vpn1.example.com", Now);
        _events.OnReconnectStopped(Now);
        Run(VpnStatus.Connecting);

        var e = _events.OnStateChanged(new VpnState(VpnStatus.Connected, Vpn1), Now);

        Assert.Equal("VPN connected", e?.Banner?.Title);
    }

    [Fact]
    public void Stopping_the_reconnect_is_recorded()
    {
        Assert.Equal("Reconnecting stopped.", _events.OnReconnectStopped(Now).Entry.Message);
    }

    [Fact]
    public void A_paused_reconnect_is_a_warning_with_a_banner()
    {
        var e = _events.OnReconnectPaused("Wrong username or password.", Now);

        Assert.Equal(NotificationSeverity.Warning, e.Entry.Severity);
        Assert.Equal("Always up paused: Wrong username or password.", e.Entry.Message);
        Assert.Equal(new Banner("Always up paused", "Wrong username or password.", NotificationSeverity.Warning), e.Banner);
    }

    private void Run(params VpnStatus[] statuses)
    {
        foreach (var status in statuses)
        {
            _events.OnStateChanged(new VpnState(status, Vpn1), Now);
        }
    }

    private static VpnState Error(VpnErrorKind kind, string message) => new(VpnStatus.Error, null, new VpnError(kind, message));
    [Fact]
    public void A_macOS_VPN_is_named_by_its_service()
    {
        var events = ConnectionEvents.ForSystemVpn("VPN");
        events.OnStateChanged(new VpnState(VpnStatus.Connecting), Now);

        var connected = events.OnStateChanged(new VpnState(VpnStatus.Connected), Now);
        var lost = events.OnStateChanged(new VpnState(VpnStatus.Error, Error: new VpnError(VpnErrorKind.TunnelFailed, "VPN lost its connection.")), Now);
        var scheduled = events.OnReconnectScheduled(new ReconnectSchedule(1, TimeSpan.FromSeconds(5), Now), host: null, Now);
        var paused = events.OnReconnectPaused("VPN refused the login.", Now);

        Assert.Equal(new NotificationEntry(Now, "macOS VPN", "Connected to VPN."), connected?.Entry);
        Assert.Equal("macOS VPN connected", connected?.Banner?.Title);
        Assert.Equal("macOS VPN connection lost", lost?.Banner?.Title);
        Assert.Equal("Reconnecting to VPN in 5 s (attempt 1).", scheduled.Entry.Message);
        Assert.Equal("Keep it up paused: VPN refused the login.", paused.Entry.Message);
    }

    [Fact]
    public void A_macOS_VPN_that_was_up_at_start_is_lost_not_failed()
    {
        var events = ConnectionEvents.ForSystemVpn("VPN", VpnStatus.Connected);

        var e = events.OnStateChanged(new VpnState(VpnStatus.Error, Error: new VpnError(VpnErrorKind.TunnelFailed, "VPN lost its connection.")), Now);

        Assert.Equal("macOS VPN connection lost", e?.Banner?.Title);
    }

    [Fact]
    public void A_macOS_VPN_switched_off_is_recorded_by_name()
    {
        var events = ConnectionEvents.ForSystemVpn("VPN");
        events.OnStateChanged(new VpnState(VpnStatus.Connected), Now);

        var e = events.OnStateChanged(VpnState.Disconnected, Now);

        Assert.Equal("Disconnected from VPN.", e?.Entry.Message);
        Assert.Null(e?.Banner);
    }
}
