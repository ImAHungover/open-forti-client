using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.Core.Engine;

/// <summary>
/// Everything the UI needs from a VPN implementation. openfortivpn is the first one;
/// keeping the UI behind this interface lets us replace it later.
/// </summary>
public interface IVpnEngine
{
    VpnState State { get; }

    /// <summary>Raised on a background thread.</summary>
    event EventHandler<VpnState>? StateChanged;

    /// <summary>
    /// A gateway failed in a way that makes trying the next one worthwhile (unreachable, tunnel not
    /// set up). Not raised for a rejected login. Raised on a background thread.
    /// </summary>
    event EventHandler<GatewayFailure>? GatewayFailed;

    /// <summary>Sanitised engine output, raised on a background thread.</summary>
    event EventHandler<string>? LogLine;

    /// <summary>
    /// Connects using the profile's gateways in order. Completes when the tunnel is up or
    /// every gateway failed; check <see cref="State"/> for the result.
    /// </summary>
    Task ConnectAsync(VpnProfile profile, VpnCredentials credentials, CancellationToken ct = default);

    Task DisconnectAsync();
}
