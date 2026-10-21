namespace OpenFortiClient.Core.Engine;

/// <summary>
/// A connection whose state can be watched: the VPN engine, or a macOS VPN service
/// (<see cref="SystemVpnConnection"/>). <see cref="ReconnectSupervisor"/> keeps either one up.
/// </summary>
public interface IConnectionState
{
    VpnState State { get; }

    /// <summary>Raised on a background thread.</summary>
    event EventHandler<VpnState>? StateChanged;
}
