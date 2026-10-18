using OpenFortiClient.Core.Notifications;

namespace OpenFortiClient.App.Services;

/// <summary>macOS notification banners. Failures are logged, never thrown: a banner must not break a connection.</summary>
public interface ISystemNotifier
{
    /// <returns>Whether banners are allowed. Asks the user the first time only.</returns>
    Task<bool> RequestPermissionAsync();

    Task ShowAsync(Banner banner);
}
