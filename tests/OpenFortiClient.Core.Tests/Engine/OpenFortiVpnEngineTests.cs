using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Engine.OpenFortiVpn;
using OpenFortiClient.Core.Profiles;
using OpenFortiClient.Core.Tests.Fakes;
using OpenFortiClient.Core.Tests.Profiles;

namespace OpenFortiClient.Core.Tests.Engine;

public sealed class OpenFortiVpnEngineTests : IDisposable
{
    private const string Digest = "4daa8c5fe6c8b1a1f0e3c2d4b5a69788796a5b4c3d2e1f0a9b8c7d6e5f4a3b2c";

    private static readonly VpnCredentials Credentials = new("jane", "s3cret");

    private readonly TempDirectory _temp = new();
    private readonly List<VpnState> _states = [];

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Connects_through_the_first_gateway()
    {
        var launcher = new FakeLauncher(p => p.Emit(
            "INFO:   Connected to gateway.",
            "INFO:   Authenticated.",
            "INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(new VpnState(VpnStatus.Connected, VpnProfileTests.Valid.Gateways[0]), engine.State);
        Assert.Equal(
            [VpnStatus.Connecting, VpnStatus.Authenticating, VpnStatus.Connected],
            _states.Select(s => s.Status));

        var process = Assert.Single(launcher.Started);
        Assert.Contains("host = vpn1.example.com", process.ConfigContent);
        Assert.Contains("password = s3cret", process.ConfigContent);
        Assert.DoesNotContain(Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories), f => !f.EndsWith("openfortivpn"));
    }

    [Fact]
    public async Task Tries_the_next_gateway_when_one_does_not_answer()
    {
        var launcher = new FakeLauncher(
            _ => { }, // vpn1 never answers
            p => p.Emit("INFO:   Connected to gateway.", "INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(VpnStatus.Connected, engine.State.Status);
        Assert.Equal("vpn2.example.com", engine.State.Gateway?.Host);
        Assert.Equal(2, launcher.Started.Count);
    }

    [Fact]
    public async Task Reports_each_failed_gateway_with_the_one_tried_next()
    {
        var launcher = new FakeLauncher(
            p => p.Emit("ERROR:  connect: Connection refused").Exit(1),
            p => p.Emit("INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);
        var failures = new List<GatewayFailure>();
        engine.GatewayFailed += (_, failure) => failures.Add(failure);

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        var failure = Assert.Single(failures);
        Assert.Equal("vpn1.example.com", failure.Gateway.Host);
        Assert.Equal("vpn2.example.com", failure.Next?.Host);
        Assert.Equal(VpnErrorKind.GatewayUnreachable, failure.Error.Kind);
    }

    [Fact]
    public async Task Reports_the_last_gateway_failing_without_a_next_one()
    {
        var launcher = new FakeLauncher(
            p => p.Emit("ERROR:  connect: Connection refused").Exit(1),
            p => p.Emit("ERROR:  connect: Connection refused").Exit(1));
        var engine = CreateEngine(launcher);
        var failures = new List<GatewayFailure>();
        engine.GatewayFailed += (_, failure) => failures.Add(failure);

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(["vpn2.example.com", null], failures.Select(f => f.Next?.Host));
    }

    [Fact]
    public async Task Does_not_report_a_gateway_failure_for_a_wrong_password()
    {
        var launcher = new FakeLauncher(p => p
            .Emit("INFO:   Connected to gateway.", "ERROR:  Could not authenticate to gateway. Please check the password, client certificate, etc.")
            .Exit(1));
        var engine = CreateEngine(launcher);
        var failures = new List<GatewayFailure>();
        engine.GatewayFailed += (_, failure) => failures.Add(failure);

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Tries_the_next_gateway_when_the_connection_fails()
    {
        var launcher = new FakeLauncher(
            p => p.Emit("ERROR:  connect: Connection refused").Exit(1),
            p => p.Emit("INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(VpnStatus.Connected, engine.State.Status);
    }

    [Fact]
    public async Task Does_not_try_other_gateways_after_a_login_failure()
    {
        var launcher = new FakeLauncher(p => p
            .Emit("INFO:   Connected to gateway.", "ERROR:  Could not authenticate to gateway. Please check the password, client certificate, etc.")
            .Exit(1));
        var engine = CreateEngine(launcher);

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(VpnStatus.Error, engine.State.Status);
        Assert.Equal(VpnErrorKind.AuthenticationFailed, engine.State.Error?.Kind);
        Assert.Single(launcher.Started);
    }

    [Fact]
    public async Task Reports_an_untrusted_certificate_with_its_digest()
    {
        var launcher = new FakeLauncher(p => p
            .Emit(
                "ERROR:  Gateway certificate validation failed, and the certificate digest is not in the local whitelist. If you trust it, rerun with:",
                $"ERROR:      --trusted-cert {Digest}",
                "ERROR:  or add this line to your configuration file:")
            .Exit(1));
        var engine = CreateEngine(launcher);

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(VpnErrorKind.UntrustedCertificate, engine.State.Error?.Kind);
        Assert.Equal(Digest, engine.State.Error?.CertificateDigest);
        Assert.Single(launcher.Started);
    }

    [Fact]
    public async Task Answers_the_one_time_password_prompt()
    {
        var launcher = new FakeLauncher(p =>
        {
            p.OnInput = (process, _) => process.Emit("INFO:   Tunnel is up and running.");
            p.Emit("INFO:   Connected to gateway.", "Two-factor authentication token: ");
        });
        var engine = CreateEngine(launcher);
        var credentials = Credentials with { OtpProvider = _ => Task.FromResult<string?>("123456") };

        await engine.ConnectAsync(VpnProfileTests.Valid, credentials);

        Assert.Equal(VpnStatus.Connected, engine.State.Status);
        Assert.Equal(["123456"], launcher.Started[0].Input);
    }

    [Fact]
    public async Task Stops_when_a_one_time_password_is_needed_but_not_given()
    {
        var launcher = new FakeLauncher(p => p.Emit("INFO:   Connected to gateway.", "Two-factor authentication token: "));
        var engine = CreateEngine(launcher);

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(VpnErrorKind.OtpRequired, engine.State.Error?.Kind);
        Assert.True(launcher.Started[0].Terminated);
    }

    [Fact]
    public async Task Disconnect_terminates_openfortivpn()
    {
        var launcher = new FakeLauncher(p => p.Emit("INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);
        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        await engine.DisconnectAsync();

        Assert.True(launcher.Started[0].Terminated);
        Assert.Equal(VpnState.Disconnected, engine.State);
        Assert.Equal([VpnStatus.Disconnecting, VpnStatus.Disconnected], _states.TakeLast(2).Select(s => s.Status));
    }

    [Fact]
    public async Task Reports_an_error_when_the_tunnel_drops()
    {
        var launcher = new FakeLauncher(p => p.Emit("INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);
        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);
        var dropped = WaitForStatus(engine, VpnStatus.Error);

        launcher.Started[0].Emit("INFO:   Closed connection to gateway.").Exit(1);
        await dropped;

        Assert.Equal(VpnErrorKind.TunnelFailed, engine.State.Error?.Kind);
    }

    [Fact]
    public async Task Reports_a_lost_tunnel_without_waiting_for_openfortivpn_to_exit()
    {
        // Seen with openfortivpn 1.24.1: after the link drops it keeps running, trying to log out.
        var launcher = new FakeLauncher(p => p.Emit("INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);
        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);
        var dropped = WaitForStatus(engine, VpnStatus.Error);

        launcher.Started[0].Emit("INFO:   Cancelling threads...", "INFO:   Setting ppp0 interface down.");
        await dropped;

        Assert.Equal(VpnErrorKind.TunnelFailed, engine.State.Error?.Kind);
        Assert.Contains("was lost", engine.State.Error?.Message);
        Assert.True(launcher.Started[0].Terminated);
        Assert.Equal(VpnStatus.Error, _states[^1].Status);
    }

    [Fact]
    public async Task Can_connect_again_after_a_lost_tunnel()
    {
        var launcher = new FakeLauncher(
            p => p.Emit("INFO:   Tunnel is up and running."),
            p => p.Emit("INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);
        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);
        var dropped = WaitForStatus(engine, VpnStatus.Error);
        launcher.Started[0].Emit("INFO:   Cancelling threads...");
        await dropped;

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(VpnStatus.Connected, engine.State.Status);
        Assert.Equal(2, launcher.Started.Count);
    }

    [Fact]
    public async Task Gives_up_on_a_gateway_when_the_tunnel_does_not_come_up_after_login()
    {
        Action<FakeProcess> hangsAfterLogin = p => p.Emit(
            "INFO:   Connected to gateway.",
            "INFO:   Authenticated.",
            "INFO:   Remote gateway has allocated a VPN.");
        var launcher = new FakeLauncher(hangsAfterLogin, hangsAfterLogin);
        var engine = CreateEngine(launcher);

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(VpnErrorKind.TunnelFailed, engine.State.Error?.Kind);
        Assert.Contains("answered, but the tunnel was not up", engine.State.Error?.Message);
        Assert.Equal(2, launcher.Started.Count);
    }

    [Fact]
    public async Task Ends_the_attempt_when_the_tunnel_cannot_be_set_up()
    {
        // The lines from a real session where the route to the gateway died after the login.
        var launcher = new FakeLauncher(p => p.Emit(
            "INFO:   Connected to gateway.",
            "INFO:   Authenticated.",
            "INFO:   Remote gateway has allocated a VPN.",
            "ERROR:  connect: Operation timed out",
            "ERROR:  Could not get VPN configuration (TLS error).",
            "INFO:   Closed connection to gateway."));
        var engine = CreateEngine(launcher);

        await engine.ConnectAsync(VpnProfileTests.Valid with { Gateways = [VpnProfileTests.Valid.Gateways[0]] }, Credentials);

        Assert.True(launcher.Started[0].Terminated);
        Assert.Equal(VpnErrorKind.TunnelFailed, engine.State.Error?.Kind);
        Assert.Contains("could not be set up (Could not get VPN configuration (TLS error).)", engine.State.Error?.Message);
    }

    [Fact]
    public async Task Disconnecting_while_the_one_time_password_is_asked_ends_disconnected()
    {
        // Review issue 1: the code arrives after openfortivpn exited; writing it used to throw.
        var launcher = new FakeLauncher(p => p.Emit("INFO:   Connected to gateway.", "Two-factor authentication token: "));
        var engine = CreateEngine(launcher);
        var credentials = Credentials with
        {
            OtpProvider = async _ =>
            {
                await engine.DisconnectAsync();
                return "123456";
            },
        };

        await engine.ConnectAsync(VpnProfileTests.Valid, credentials);

        Assert.Equal(VpnState.Disconnected, engine.State);
        Assert.Empty(launcher.Started[0].Input);
    }

    [Fact]
    public async Task Disconnect_gives_up_on_an_openfortivpn_that_does_not_stop()
    {
        // Review issue 2: waiting for the exit used to have no limit.
        var launcher = new FakeLauncher(p => p.Emit("INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher, disconnectTimeout: TimeSpan.FromMilliseconds(200));
        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);
        launcher.Started[0].IgnoresTerminate = true;

        await engine.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(VpnStatus.Error, engine.State.Status);
        Assert.Contains("did not stop", engine.State.Error?.Message);
        Assert.Equal(2, launcher.Started[0].TerminateCount);
    }

    [Fact]
    public async Task Disconnecting_between_two_gateways_stops_the_attempt()
    {
        // Review issue 3: with no process running, the request used to be dropped and vpn2 connected.
        var launcher = new FakeLauncher(
            p => p.Emit("ERROR:  connect: Connection refused").Exit(1),
            p => p.Emit("INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);
        engine.GatewayFailed += (_, _) => engine.DisconnectAsync().Wait();

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(VpnState.Disconnected, engine.State);
        Assert.Single(launcher.Started);
    }

    [Fact]
    public async Task Disconnecting_before_openfortivpn_starts_stops_the_attempt()
    {
        var launcher = new FakeLauncher(p => p.Emit("INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);
        engine.StateChanged += (_, state) =>
        {
            if (state.Status == VpnStatus.Connecting)
            {
                engine.DisconnectAsync().Wait();
            }
        };

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(VpnState.Disconnected, engine.State);
        Assert.Empty(launcher.Started);
    }

    [Fact]
    public async Task Reconnecting_while_the_lost_tunnel_still_shuts_down_shows_progress_and_gives_up()
    {
        // Review issue 4: the wait for the old process used to be silent and endless.
        var launcher = new FakeLauncher(p => p.Emit("INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher, disconnectTimeout: TimeSpan.FromMilliseconds(200));
        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);
        var dropped = WaitForStatus(engine, VpnStatus.Error);
        launcher.Started[0].IgnoresTerminate = true;
        launcher.Started[0].Emit("INFO:   Cancelling threads...");
        await dropped;
        _states.Clear();

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(VpnStatus.Connecting, _states[0].Status);
        Assert.Equal(VpnStatus.Error, engine.State.Status);
        Assert.Contains("still closing", engine.State.Error?.Message);
        Assert.Single(launcher.Started);
    }

    [Fact]
    public async Task Reconnecting_after_the_lost_tunnel_shut_down_connects()
    {
        var launcher = new FakeLauncher(
            p => p.Emit("INFO:   Tunnel is up and running."),
            p => p.Emit("INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);
        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);
        var dropped = WaitForStatus(engine, VpnStatus.Error);
        launcher.Started[0].Emit("INFO:   Cancelling threads...");
        await dropped;

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(VpnStatus.Connected, engine.State.Status);
    }

    [Fact]
    public async Task Passes_the_client_key_from_the_keychain_to_the_launcher()
    {
        var launcher = new FakeLauncher(p => p.Emit("INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials with { ClientKeyPem = "-----BEGIN PRIVATE KEY-----" });

        Assert.Equal("-----BEGIN PRIVATE KEY-----", launcher.Started[0].Request.ClientKeyPem);
        Assert.DoesNotContain("user-key", launcher.Started[0].ConfigContent);
    }

    [Fact]
    public async Task Refuses_a_keychain_key_that_is_missing()
    {
        // For example after restoring a backup on another Mac: backups hold no secrets.
        var launcher = new FakeLauncher();
        var engine = CreateEngine(launcher);
        var profile = VpnProfileTests.Valid with { ClientCertificatePath = null, ClientKeyInKeychain = true };

        await engine.ConnectAsync(profile, Credentials);

        Assert.Equal(VpnErrorKind.InvalidProfile, engine.State.Error?.Kind);
        Assert.Contains("choose the key file again", engine.State.Error?.Message);
        Assert.Empty(launcher.Started);
    }

    [Fact]
    public async Task Reports_a_missing_openfortivpn()
    {
        var engine = CreateEngine(new FakeLauncher(), executable: "/nonexistent/openfortivpn");

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.Equal(VpnErrorKind.EngineNotFound, engine.State.Error?.Kind);
    }

    [Fact]
    public async Task Reports_an_invalid_profile_without_starting_anything()
    {
        var launcher = new FakeLauncher();
        var engine = CreateEngine(launcher);

        await engine.ConnectAsync(VpnProfileTests.Valid with { Gateways = [] }, Credentials);

        Assert.Equal(VpnErrorKind.InvalidProfile, engine.State.Error?.Kind);
        Assert.Empty(launcher.Started);
    }

    [Fact]
    public async Task Reports_a_missing_client_certificate_without_starting_anything()
    {
        var launcher = new FakeLauncher();
        var engine = CreateEngine(launcher);
        var profile = VpnProfileTests.Valid with
        {
            ClientCertificatePath = Path.Combine(_temp.Path, "gone.pem"),
            ClientKeyPath = Path.Combine(_temp.Path, "gone.key"),
        };

        await engine.ConnectAsync(profile, Credentials);

        Assert.Equal(VpnErrorKind.InvalidProfile, engine.State.Error?.Kind);
        Assert.Contains("not found", engine.State.Error?.Message);
        Assert.Empty(launcher.Started);
    }

    [Fact]
    public async Task Log_lines_are_sanitised()
    {
        var lines = new List<string>();
        var launcher = new FakeLauncher(p => p.Emit("DEBUG:  Configuration password = \"s3cret\"", "INFO:   Tunnel is up and running."));
        var engine = CreateEngine(launcher);
        engine.LogLine += (_, line) => lines.Add(line);

        await engine.ConnectAsync(VpnProfileTests.Valid, Credentials);

        Assert.DoesNotContain(lines, l => l.Contains("s3cret"));
    }

    private OpenFortiVpnEngine CreateEngine(FakeLauncher launcher, string? executable = null, TimeSpan? disconnectTimeout = null)
    {
        if (executable is null)
        {
            executable = Path.Combine(_temp.Path, "openfortivpn");
            File.WriteAllText(executable, "");
        }

        var engine = new OpenFortiVpnEngine(launcher, new OpenFortiVpnEngineOptions
        {
            ExecutablePath = executable,
            GatewayTimeout = TimeSpan.FromMilliseconds(200),
            TunnelSetupTimeout = TimeSpan.FromMilliseconds(200),
            DisconnectTimeout = disconnectTimeout ?? TimeSpan.FromSeconds(2),
        });
        engine.StateChanged += (_, state) =>
        {
            lock (_states)
            {
                _states.Add(state);
            }
        };
        return engine;
    }

    private static Task WaitForStatus(IVpnEngine engine, VpnStatus status)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StateChanged += (_, state) =>
        {
            if (state.Status == status)
            {
                tcs.TrySetResult();
            }
        };
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
