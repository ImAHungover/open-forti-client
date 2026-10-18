using Foundation;
using ObjCRuntime;
using OpenFortiClient.App.Services;
using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Logging;
using OpenFortiClient.Core.Profiles;
using UIKit;

namespace OpenFortiClient.App;

/// <summary>
/// The menu bar icon. The drawing is AppKit code in libOFCNative.dylib (src/OpenFortiClient.Native),
/// which this class loads at runtime, feeds with the connection state, and answers through the
/// exported selectors below. If anything fails the app keeps working, just without the icon.
/// </summary>
public sealed class StatusMenu : NSObject
{
    private const string LogSource = "menu bar";

    private readonly VpnSession _session;
    private readonly IProfileStore _profiles;
    private readonly IAppLog _log;
    private NSObject? _menu;
    private IReadOnlyList<VpnProfile> _profileList = [];

    public StatusMenu(VpnSession session, IProfileStore profiles, IAppLog log)
    {
        _session = session;
        _profiles = profiles;
        _log = log;
    }

    /// <summary>Loads the library and shows the icon. Call once, on the main thread.</summary>
    public void Install()
    {
        try
        {
            _menu = NativeBridge.Create("OFCStatusMenu", out var error);
            if (_menu is null)
            {
                _log.Info(LogSource, error ?? "The menu bar library did not load.");
                return;
            }

            NativeBridge.Call(_menu, "install:", this);

            _session.StateChanged += (_, _) => Refresh();
            _session.ReconnectChanged += (_, _) => Refresh();
            Refresh();
        }
        catch (Exception ex)
        {
            _log.Info(LogSource, $"The menu bar icon is not available: {ex.Message}");
            _menu = null;
        }
    }

    /// <summary>Redraws the menu from the current state; safe from any thread.</summary>
    public void Refresh() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_menu is null)
        {
            return;
        }

        try
        {
            var profiles = await _profiles.GetAllAsync();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _profileList = profiles;
                var state = _session.State;
                var pending = _session.PendingReconnect;
                var connected = state.Status == VpnStatus.Connected;
                var reconnecting = _session.IsReconnecting && !connected && !state.IsBusy;

                var (status, kind) = state.Status switch
                {
                    VpnStatus.Connected => ($"Connected to {state.Gateway?.Host}", "connected"),
                    VpnStatus.Connecting or VpnStatus.Authenticating => ($"Connecting to {state.Gateway?.Host}…", "busy"),
                    VpnStatus.Disconnecting => ("Disconnecting…", "busy"),
                    _ when pending is not null => ($"Reconnecting at {pending.At.ToLocalTime():HH:mm:ss} (attempt {pending.Attempt})", "reconnecting"),
                    VpnStatus.Error => ($"Not connected: {state.Error?.Message}", "error"),
                    _ => ("Not connected", "disconnected"),
                };

                var info = new NSMutableDictionary
                {
                    ["status"] = new NSString(status),
                    ["state"] = new NSString(kind),
                    ["profiles"] = NSArray.FromStrings([.. profiles.Select(p => p.Name)]),
                    ["canConnect"] = NSNumber.FromBoolean(!connected && !state.IsBusy),
                    ["canDisconnect"] = NSNumber.FromBoolean(connected || state.IsBusy || reconnecting),
                    ["disconnectTitle"] = new NSString(reconnecting ? "Stop reconnecting" : "Disconnect"),
                };
                NativeBridge.Call(_menu, "update:", info);
            });
        }
        catch (Exception ex)
        {
            _log.Info(LogSource, $"Could not update the menu bar icon: {ex.Message}");
        }
    }

    [Export("statusMenuConnect:")]
    public void Connect(NSNumber index)
    {
        if (index.Int32Value >= 0 && index.Int32Value < _profileList.Count)
        {
            var profile = _profileList[index.Int32Value];
            _ = Run(async () =>
            {
                if (!await _session.ConnectFromMenuAsync(profile))
                {
                    // No saved password: the window asks for it.
                    await MainThread.InvokeOnMainThreadAsync(() => Open(null));
                }
            });
        }
    }

    [Export("statusMenuDisconnect:")]
    public void Disconnect(NSObject? sender) => _ = Run(_session.DisconnectAsync);

    [Export("statusMenuOpen:")]
    public void Open(NSObject? sender)
    {
        // Closing the window ends its scene; ask for a new one if none is left.
        if (!UIApplication.SharedApplication.ConnectedScenes.ToArray().OfType<UIWindowScene>().Any())
        {
            UIApplication.SharedApplication.ActivateSceneSession(UISceneSessionActivationRequest.Create(), error => _log.Info(LogSource, $"Could not open a window: {error.LocalizedDescription}"));
        }
    }

    /// <summary>Quits; MauiProgram's WillTerminate hook disconnects first, as for any other way of quitting.</summary>
    [Export("statusMenuQuit:")]
    public void Quit(NSObject? sender)
    {
        _log.Info(LogSource, "Quit from the menu bar.");

        // Terminating while the menu is still open stalls UIKit's scene teardown, and its watchdog then
        // kills the app before WillTerminate can disconnect. So quit once the menu has closed.
        _ = Task.Delay(TimeSpan.FromMilliseconds(300)).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_menu is not null)
                {
                    NativeBridge.Call(_menu, "terminate:");
                }
            }));
    }

    private async Task Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _log.Info(LogSource, $"Menu action failed: {ex.Message}");
        }
    }
}
