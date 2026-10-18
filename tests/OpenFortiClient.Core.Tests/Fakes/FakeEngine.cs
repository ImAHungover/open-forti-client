using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.Core.Tests.Fakes;

/// <summary>An engine whose state the test sets directly.</summary>
internal sealed class FakeEngine : IVpnEngine
{
    public VpnState State { get; private set; } = VpnState.Disconnected;

    public event EventHandler<VpnState>? StateChanged;

    public event EventHandler<string>? LogLine;

    public event EventHandler<GatewayFailure>? GatewayFailed;

    public void Set(VpnStatus status, VpnErrorKind? error = null)
    {
        State = new VpnState(status, new Uri("https://vpn1.example.com"), error is { } kind ? new VpnError(kind, $"{kind} happened.") : null);
        StateChanged?.Invoke(this, State);
    }

    public Task ConnectAsync(VpnProfile profile, VpnCredentials credentials, CancellationToken ct = default) => Task.CompletedTask;

    public Task DisconnectAsync() => Task.CompletedTask;

    // Keep the compiler quiet about the unused events.
    internal void Unused() => (LogLine, GatewayFailed) = (null, null);
}
