using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenFortiClient.Core.Engine.OpenFortiVpn;
using OpenFortiClient.Core.Settings;

namespace OpenFortiClient.App.ViewModels;

/// <summary>Version and licence information (flows.md: Information).</summary>
public sealed partial class AboutViewModel(ISettingsStore settings) : ObservableObject
{
    public string AppVersion => AppInfo.Current.VersionString;

    [ObservableProperty]
    public partial string OpenFortiVpnVersion { get; set; } = "…";

    [RelayCommand]
    private async Task LoadAsync()
    {
        var path = (await settings.GetAsync()).OpenFortiVpnPath;
        OpenFortiVpnVersion = await OpenFortiVpnInfo.GetVersionAsync(path) ?? $"not found at {path}";
    }

    /// <summary>ResultLab's product page.</summary>
    public const string ResultLabProductsUrl = "https://resultlab.io/products";

    [RelayCommand]
    private Task OpenResultLabAsync() => Launcher.Default.OpenAsync(ResultLabProductsUrl);

    [RelayCommand]
    private Task OpenLicenseAsync() => Launcher.Default.OpenAsync("https://opensource.org/license/mit");

    [RelayCommand]
    private Task OpenProjectAsync() =>
        Launcher.Default.OpenAsync("https://github.com/adrienverge/openfortivpn");
}
