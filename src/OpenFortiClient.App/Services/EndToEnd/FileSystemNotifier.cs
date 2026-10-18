#if E2E
using OpenFortiClient.Core.Notifications;

namespace OpenFortiClient.App.Services.EndToEnd;

/// <summary>Records banners in system-notifications.log ("severity|title|body") instead of showing them.</summary>
public sealed class FileSystemNotifier(string directory) : ISystemNotifier
{
    private readonly Lock _lock = new();

    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);

    public Task ShowAsync(Banner banner)
    {
        lock (_lock)
        {
            File.AppendAllText(Path.Combine(directory, "system-notifications.log"), $"{banner.Severity}|{banner.Title}|{banner.Body}\n");
        }

        return Task.CompletedTask;
    }
}
#endif
