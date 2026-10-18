using Foundation;
using OpenFortiClient.App.Services;
using OpenFortiClient.Core.Logging;
using OpenFortiClient.Core.Notifications;
using UserNotifications;

namespace OpenFortiClient.App;

/// <summary>Banners through the macOS notification centre (UNUserNotificationCenter).</summary>
public sealed class UserNotificationsNotifier : ISystemNotifier
{
    private const string LogSource = "notifications";

    private readonly IAppLog _log;

    public UserNotificationsNotifier(IAppLog log)
    {
        _log = log;

        // Without a delegate, macOS drops banners while the app is in front.
        UNUserNotificationCenter.Current.Delegate = new ForegroundDelegate();
    }

    public async Task<bool> RequestPermissionAsync()
    {
        try
        {
            var (granted, error) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
                UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound);
            if (error is not null)
            {
                _log.Info(LogSource, $"Notification permission failed: {error.LocalizedDescription}");
            }

            return granted;
        }
        catch (Exception ex)
        {
            _log.Info(LogSource, $"Notification permission failed: {ex.Message}");
            return false;
        }
    }

    public async Task ShowAsync(Banner banner)
    {
        try
        {
            var content = new UNMutableNotificationContent { Title = banner.Title, Body = banner.Body };
            if (banner.Severity == NotificationSeverity.Error)
            {
                content.Sound = UNNotificationSound.Default;
            }

            var request = UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString(), content, trigger: null);
            await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request);
        }
        catch (Exception ex)
        {
            _log.Info(LogSource, $"Could not show a notification: {ex.Message}");
        }
    }

    private sealed class ForegroundDelegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(
            UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler) =>
            completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Sound);
    }
}
