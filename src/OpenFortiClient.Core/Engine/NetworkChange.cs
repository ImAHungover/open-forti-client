namespace OpenFortiClient.Core.Engine;

/// <summary>What changed in the Mac's network since the last report (collected over a short debounce window).</summary>
/// <param name="BecameUsable">The network was unusable and is usable again.</param>
/// <param name="Added">Interfaces that appeared, for example en7 or ppp0.</param>
/// <param name="Removed">Interfaces that went away.</param>
public sealed record NetworkChange(bool BecameUsable, IReadOnlyList<string> Added, IReadOnlyList<string> Removed);

/// <summary>Decides whether a network change is worth retrying a waiting reconnect at once.</summary>
public static class NetworkChangePolicy
{
    /// <remarks>
    /// Only something that can make the gateway reachable again counts: the network coming back, a new
    /// interface, or another VPN coming up (the company L2TP VPN's ppp0). Interfaces going away never do,
    /// and neither does a tunnel interface appearing while openfortivpn starts: that one is the app's own,
    /// and retrying on it would restart openfortivpn every couple of seconds.
    /// </remarks>
    public static bool ShouldRetry(NetworkChange change, VpnStatus status)
    {
        if (change.BecameUsable)
        {
            return true;
        }

        var ownTunnelMayAppear = status is VpnStatus.Connecting or VpnStatus.Authenticating or VpnStatus.Connected;
        return change.Added.Any(name => !IsTunnel(name) || !ownTunnelMayAppear);
    }

    /// <summary>ppp (openfortivpn, L2TP), utun (IKEv2, WireGuard) and ipsec interfaces.</summary>
    public static bool IsTunnel(string name) =>
        name.StartsWith("ppp", StringComparison.Ordinal)
        || name.StartsWith("utun", StringComparison.Ordinal)
        || name.StartsWith("ipsec", StringComparison.Ordinal);
}
