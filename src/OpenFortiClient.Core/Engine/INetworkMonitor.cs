namespace OpenFortiClient.Core.Engine;

/// <summary>Tells when the Mac's network changed, so a waiting reconnect can try again at once.</summary>
public interface INetworkMonitor
{
    /// <summary>Raised on a background thread; <see cref="NetworkChangePolicy"/> decides whether it matters.</summary>
    event EventHandler<NetworkChange>? Changed;

    void Start();
}
