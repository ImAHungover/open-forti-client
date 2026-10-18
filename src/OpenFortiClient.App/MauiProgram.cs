using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using OpenFortiClient.App.Services;
#if E2E
using OpenFortiClient.App.Services.EndToEnd;
#endif
using OpenFortiClient.App.ViewModels;
using OpenFortiClient.App.Views;
using OpenFortiClient.Core;
using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Engine.OpenFortiVpn;
using OpenFortiClient.Core.Logging;
using OpenFortiClient.Core.Notifications;
using OpenFortiClient.Core.Profiles;
using OpenFortiClient.Core.Secrets;
using OpenFortiClient.Core.Settings;

namespace OpenFortiClient.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // ResultLab typography (resultlab.io): Inter for text, JetBrains Mono for labels. Both SIL OFL 1.1,
        // licences in third-party/fonts.
        builder.ConfigureFonts(fonts =>
        {
            fonts.AddFont("Inter-Regular.ttf", "Inter");
            fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");
            fonts.AddFont("JetBrainsMono-Regular.ttf", "Mono");
            fonts.AddFont("JetBrainsMono-Medium.ttf", "MonoMedium");
        });

        // However the app quits (menu bar, Cmd-Q, logout), never leave openfortivpn running.
        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios.WillTerminate(_ =>
            IPlatformApplication.Current?.Services.GetService<VpnSession>()?.DisconnectBeforeQuit())));

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var services = builder.Services;
        var paths = AppPaths.ForCurrentUser();
#if E2E
        if (EndToEndMode.Directory is { } testDirectory)
        {
            paths = EndToEndMode.Paths(testDirectory);
        }
#endif

        // Core
        services.AddSingleton(paths);
        services.AddSingleton<IProfileStore, JsonProfileStore>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<INotificationHistory>(_ => new JsonNotificationHistory(paths));
        services.AddSingleton(_ => new FileAppLog(paths));
        services.AddSingleton<IAppLog>(sp => sp.GetRequiredService<FileAppLog>());
        services.AddSingleton<ConfigurationBackupService>();

        // Elevation: the privileged helper, or sudo in development builds (ElevationLauncher decides).
        services.AddSingleton<IHelperControl, HelperControl>();
        services.AddSingleton(sp => new ElevationLauncher(
            new SudoLauncher(paths.RuntimeDirectory), new HelperLauncher(), sp.GetRequiredService<IHelperControl>()));
        services.AddSingleton<IPrivilegedLauncher>(sp => sp.GetRequiredService<ElevationLauncher>());

        // Engine. Defaults until VpnSession.InitializeAsync applies the saved settings.
        services.AddSingleton(sp => new OpenFortiVpnEngine(
            sp.GetRequiredService<IPrivilegedLauncher>(),
            OpenFortiVpnEngineOptions.From(new AppSettings(), paths)));
        services.AddSingleton<IVpnEngine>(sp => sp.GetRequiredService<OpenFortiVpnEngine>());

        // App services
        services.AddSingleton<ISecretStore, SecureStorageSecretStore>();
        services.AddSingleton<IDialogService, ShellDialogService>();
        services.AddSingleton<ISystemNotifier, UserNotificationsNotifier>();
        services.AddSingleton<INetworkMonitor, PathNetworkMonitor>();
        services.AddSingleton<VpnSession>();
        services.AddSingleton<StatusMenu>();

#if E2E
        // End-to-end UI tests: no root, no Keychain, scripted openfortivpn behind a fake helper (registered
        // last, so these win).
        if (EndToEndMode.Directory is { } directory)
        {
            var scripted = new ScriptedLauncher(directory);
            services.AddSingleton<IHelperControl>(new FakeHelperControl(directory));
            services.AddSingleton(sp => new ElevationLauncher(scripted, scripted, sp.GetRequiredService<IHelperControl>()));
            services.AddSingleton<ISecretStore>(new FileSecretStore(directory));
            services.AddSingleton<ISystemNotifier>(new FileSystemNotifier(directory));
            services.AddSingleton<INetworkMonitor>(new FileNetworkMonitor(directory));
        }
#endif

        // View models hold the state and are singletons. Pages are made per window: a window closed to the
        // menu bar keeps its pages, so the window that "Open OpenFortiClient" brings back needs new ones.
        services.AddSingleton<ConnectViewModel>();
        services.AddTransient<ConnectPage>();
        services.AddTransient<ConnectionEditViewModel>();
        services.AddTransient<ConnectionEditPage>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<SettingsPage>();
        services.AddSingleton<NotificationsViewModel>();
        services.AddTransient<NotificationsPage>();
        services.AddSingleton<AboutViewModel>();
        services.AddTransient<AboutPage>();

        return builder.Build();
    }
}
