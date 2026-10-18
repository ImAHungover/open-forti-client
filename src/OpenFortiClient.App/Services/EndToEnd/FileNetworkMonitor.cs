#if E2E
using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.App.Services.EndToEnd;

/// <summary>The network coming back whenever the test creates the file "network-change" (AppSession.Trigger).</summary>
public sealed class FileNetworkMonitor(string directory) : INetworkMonitor
{
    public event EventHandler<NetworkChange>? Changed;

    public void Start() => _ = WatchAsync();

    private async Task WatchAsync()
    {
        var trigger = Path.Combine(directory, "network-change");
        while (true)
        {
            await Task.Delay(100);
            if (File.Exists(trigger))
            {
                File.Delete(trigger);
                Changed?.Invoke(this, new NetworkChange(BecameUsable: true, [], []));
            }
        }
    }
}
#endif
