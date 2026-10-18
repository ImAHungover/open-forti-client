using OpenFortiClient.App.Services;
using OpenFortiClient.Core.Logging;

namespace OpenFortiClient.App;

public partial class App : Application
{
    private readonly VpnSession _session;
    private readonly StatusMenu _statusMenu;
    private readonly OpenFortiClient.Core.Logging.IAppLog _log;
    private bool _started;

    public App(VpnSession session, StatusMenu statusMenu, OpenFortiClient.Core.Logging.IAppLog log)
    {
        InitializeComponent();

        // The ResultLab look is dark only, like resultlab.io.
        UserAppTheme = AppTheme.Dark;
        _session = session;
        _statusMenu = statusMenu;
        _log = log;
    }

    // Called again when the menu bar's "Open OpenFortiClient" brings back a closed window.
    protected override Window CreateWindow(IActivationState? activationState)
    {
        if (!_started)
        {
            _started = true;
            _ = _session.InitializeAsync();
            _statusMenu.Install();
        }

        var window = new Window(new AppShell()) { Title = "OpenFortiClient" };

        // The size is set on the native window scene once it exists (see WindowSizing).
        var sized = false;
        window.Activated += (_, _) =>
        {
            if (!sized && window.Handler?.PlatformView is UIKit.UIWindow { WindowScene: { } scene })
            {
                sized = true;
                WindowSizing.Apply(scene, _log);
            }
        };
        return window;
    }
}
