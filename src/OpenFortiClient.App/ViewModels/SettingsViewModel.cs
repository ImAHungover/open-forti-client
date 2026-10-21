using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenFortiClient.App.Services;
using OpenFortiClient.Core.Logging;
using OpenFortiClient.Core.Settings;

namespace OpenFortiClient.App.ViewModels;

/// <summary>Settings screen (flows.md: Settings Menu).</summary>
public sealed partial class SettingsViewModel(
    ISettingsStore settings,
    VpnSession session,
    SystemVpnSession systemVpns,
    ConfigurationBackupService backup,
    FileAppLog log,
    IDialogService dialogs,
    IHelperControl helper) : ObservableObject
{
#if DEBUG
    /// <summary>Development builds can choose sudo or the helper; release builds always use the helper.</summary>
    public bool IsDevelopmentBuild => true;
#else
    public bool IsDevelopmentBuild => false;
#endif

    [ObservableProperty]
    public partial bool UsePrivilegedHelper { get; set; }

    [ObservableProperty]
    public partial string HelperStatusText { get; set; } = "";

    [ObservableProperty]
    public partial bool CanInstallHelper { get; set; }

    [ObservableProperty]
    public partial bool CanRemoveHelper { get; set; }

    [ObservableProperty]
    public partial bool HelperNeedsApproval { get; set; }

    public IReadOnlyList<AppLogLevel> LogLevels { get; } = Enum.GetValues<AppLogLevel>();

    [ObservableProperty]
    public partial AppLogLevel LogLevel { get; set; }

    [ObservableProperty]
    public partial bool WarnInvalidServerCertificate { get; set; } = true;

    [ObservableProperty]
    public partial string OpenFortiVpnPath { get; set; } = "";

    [ObservableProperty]
    public partial int GatewayTimeoutSeconds { get; set; } = 30;

    [ObservableProperty]
    public partial bool SetDns { get; set; } = true;

    [ObservableProperty]
    public partial bool SetRoutes { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowSystemNotifications { get; set; } = true;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var current = await settings.GetAsync();
        LogLevel = current.LogLevel;
        WarnInvalidServerCertificate = current.WarnInvalidServerCertificate;
        OpenFortiVpnPath = current.OpenFortiVpnPath;
        GatewayTimeoutSeconds = current.GatewayTimeoutSeconds;
        SetDns = current.SetDns;
        SetRoutes = current.SetRoutes;
        ShowSystemNotifications = current.ShowSystemNotifications;
        UsePrivilegedHelper = current.UsePrivilegedHelper;
        RefreshHelper();
    }

    [RelayCommand]
    private void RefreshHelper()
    {
        var status = helper.Status;
        HelperStatusText = status switch
        {
            HelperStatus.Enabled => "Installed and allowed.",
            HelperStatus.RequiresApproval => "Installed, waiting for your approval in System Settings → General → Login Items.",
            HelperStatus.NotRegistered => "Not installed.",
            _ => "Not available in this build.",
        };
        CanInstallHelper = status == HelperStatus.NotRegistered;
        CanRemoveHelper = status is HelperStatus.Enabled or HelperStatus.RequiresApproval;
        HelperNeedsApproval = status == HelperStatus.RequiresApproval;
    }

    [RelayCommand]
    private async Task InstallHelperAsync()
    {
        if (helper.Register() is { } error)
        {
            await dialogs.AlertAsync("Privileged helper", $"Could not install the helper: {error}");
        }
        else if (helper.Status == HelperStatus.RequiresApproval)
        {
            helper.OpenLoginItemsSettings();
        }

        RefreshHelper();
    }

    [RelayCommand]
    private async Task RemoveHelperAsync()
    {
        if (helper.Unregister() is { } error)
        {
            await dialogs.AlertAsync("Privileged helper", $"Could not remove the helper: {error}");
        }

        RefreshHelper();
    }

    [RelayCommand]
    private void OpenLoginItems() => helper.OpenLoginItemsSettings();

    [RelayCommand]
    private async Task SaveAsync()
    {
        // From the stored settings, so the ones not on this page (reconnect delays) are kept.
        var updated = await settings.GetAsync() with
        {
            UsePrivilegedHelper = UsePrivilegedHelper,
            LogLevel = LogLevel,
            WarnInvalidServerCertificate = WarnInvalidServerCertificate,
            OpenFortiVpnPath = OpenFortiVpnPath.Trim(),
            GatewayTimeoutSeconds = Math.Clamp(GatewayTimeoutSeconds, 5, 300),
            SetDns = SetDns,
            SetRoutes = SetRoutes,
            ShowSystemNotifications = ShowSystemNotifications,
        };

        await settings.SaveAsync(updated);
        await session.ApplySettingsAsync(updated);
        await systemVpns.ApplySettingsAsync(updated);
        await dialogs.AlertAsync("Settings", "Settings saved. They apply from the next connection.");
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        var path = Path.Combine(DownloadsFolder, $"openforticlient-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await using (var stream = File.Create(path))
        {
            await backup.ExportAsync(stream);
        }

        await dialogs.AlertAsync("Backup", $"Connections and settings were saved to\n{path}\n\nPasswords are not included.");
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a backup file" });
        if (file is null
            || !await dialogs.ConfirmAsync("Restore", "Replace all connections and settings with the backup?", "Restore"))
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenReadAsync();
            await backup.ImportAsync(stream);
            await LoadAsync();
            var restored = await settings.GetAsync();
            await session.ApplySettingsAsync(restored);
            await systemVpns.ApplySettingsAsync(restored);
            await dialogs.AlertAsync("Restore", "The backup was restored.");
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            await dialogs.AlertAsync("Restore failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        var path = Path.Combine(DownloadsFolder, $"openforticlient-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        log.Export(path);
        await dialogs.AlertAsync("Export logs", $"Logs were saved to\n{path}");
    }

    [RelayCommand]
    private async Task ClearLogsAsync()
    {
        if (await dialogs.ConfirmAsync("Clear logs", "Delete all log files?", "Delete"))
        {
            log.Clear();
        }
    }

    private static string DownloadsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}
