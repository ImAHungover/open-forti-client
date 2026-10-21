using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenFortiClient.App.Services;
using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.App.ViewModels;

/// <summary>
/// "macOS VPN settings": the editable settings of an L2TP service set up in System Settings. The password and
/// the shared secret are never shown; left empty, they stay as saved. Saving goes through the privileged helper.
/// </summary>
public sealed partial class SystemVpnEditViewModel(SystemVpnSession session) : ObservableObject, IQueryAttributable
{
    public const string ServiceIdKey = "id";

    private string? _id;

    [ObservableProperty]
    public partial string Title { get; set; } = "macOS VPN settings";

    [ObservableProperty]
    public partial string Server { get; set; } = "";

    [ObservableProperty]
    public partial string Account { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial string SharedSecret { get; set; } = "";

    [ObservableProperty]
    public partial bool SendAllTraffic { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeadAfterText))]
    public partial string EchoInterval { get; set; } = "20";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeadAfterText))]
    public partial string EchoFailures { get; set; } = "15";

    /// <summary>What the keep-alive values mean for Keep it up.</summary>
    public string DeadAfterText => int.TryParse(EchoInterval, out var interval) && int.TryParse(EchoFailures, out var failures)
        ? $"A dead connection is noticed after {interval * failures} s."
        : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>Saved while the VPN is connected: it uses the new settings from its next connect.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSaveButtons))]
    public partial bool ShowReconnect { get; set; }

    public bool ShowSaveButtons => !ShowReconnect;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsSaving { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _id = query.TryGetValue(ServiceIdKey, out var value) ? value as string : null;
        query.Clear();
        Load();
    }

    private void Load()
    {
        ErrorText = null;
        ShowReconnect = false;
        Password = "";
        SharedSecret = "";
        var item = session.Items.FirstOrDefault(i => i.Service.Id == _id);
        if (_id is null || item is null || session.Settings(_id) is not { } settings)
        {
            ErrorText = "This VPN cannot be changed here.";
            return;
        }

        Title = $"{item.Service.Name} settings";
        Server = settings.Server;
        Account = settings.Account;
        SendAllTraffic = settings.SendAllTraffic;
        EchoInterval = settings.EchoIntervalSeconds.ToString();
        EchoFailures = settings.EchoFailures.ToString();
    }

    private bool CanSave() => !IsSaving;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_id is null)
        {
            return;
        }

        if (!int.TryParse(EchoInterval, out var interval) || !int.TryParse(EchoFailures, out var failures))
        {
            ErrorText = "Keep-alive values must be whole numbers.";
            return;
        }

        var change = new SystemVpnSettingsChange(
            new SystemVpnSettings(Server.Trim(), Account.Trim(), SendAllTraffic, interval, failures),
            Password.Length == 0 ? null : Password,
            SharedSecret.Length == 0 ? null : SharedSecret);

        IsSaving = true;
        try
        {
            ErrorText = await session.SaveSettingsAsync(_id, change);
        }
        finally
        {
            IsSaving = false;
        }

        if (ErrorText is not null)
        {
            return;
        }

        Password = "";
        SharedSecret = "";
        var connected = session.Items.FirstOrDefault(i => i.Service.Id == _id)?.Current.Status == SystemVpnStatus.Connected;
        if (connected)
        {
            ShowReconnect = true;
            return;
        }

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        if (_id is not null)
        {
            await session.ReconnectAsync(_id);
        }

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");
}
