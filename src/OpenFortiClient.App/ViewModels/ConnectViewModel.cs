using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenFortiClient.App.Services;
using OpenFortiClient.App.Views;
using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.App.ViewModels;

/// <summary>The main "Connect" screen (flows.md: Main / Connect view).</summary>
public sealed partial class ConnectViewModel : ObservableObject
{
    private const string AddOption = "Add a new connection";
    private const string EditOption = "Edit the selected connection";
    private const string DeleteOption = "Delete the selected connection";
    private const string LastProfileKey = "last-profile-id";

    private readonly IProfileStore _profiles;
    private readonly VpnSession _session;
    private readonly IDialogService _dialogs;
    private IDispatcherTimer? _countdown;
    private bool _autoConnectTried;

    public ConnectViewModel(IProfileStore profiles, VpnSession session, IDialogService dialogs)
    {
        _profiles = profiles;
        _session = session;
        _dialogs = dialogs;
        _session.StateChanged += (_, state) => MainThread.BeginInvokeOnMainThread(() => ApplyState(state));
        _session.ReconnectChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() => ApplyState(_session.State));
        ApplyState(_session.State);
    }

    public ObservableCollection<VpnProfile> Profiles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial VpnProfile? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial bool SavePassword { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Not connected";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoginFields), nameof(ShowDisconnect))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoginFields), nameof(ShowDisconnect), nameof(DisconnectText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>"Always up" is waiting to reconnect, or reconnecting.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoginFields), nameof(ShowDisconnect), nameof(DisconnectText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand))]
    public partial bool IsReconnecting { get; set; }

    public string DisconnectText => IsReconnecting && !IsConnected && !IsBusy ? "Stop reconnecting" : "Disconnect";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyHint))]
    public partial bool HasProfiles { get; set; }

    // While a reconnect waits, both stay: Connect replaces it, "Stop reconnecting" ends it.
    public bool ShowLoginFields => !IsConnected && !IsBusy;

    public bool ShowDisconnect => IsConnected || IsBusy || IsReconnecting;

    public bool ShowEmptyHint => !HasProfiles;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    [RelayCommand]
    private async Task LoadAsync()
    {
        var selectedId = SelectedProfile?.Id
            ?? (Guid.TryParse(Preferences.Default.Get(LastProfileKey, ""), out var last) ? last : (Guid?)null);

        Profiles.Clear();
        foreach (var profile in await _profiles.GetAllAsync())
        {
            Profiles.Add(profile);
        }

        HasProfiles = Profiles.Count > 0;
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId) ?? Profiles.FirstOrDefault();

        // "Connect when OpenFortiClient starts": once, when the screen first appears.
        if (!_autoConnectTried)
        {
            _autoConnectTried = true;
            if (Profiles.FirstOrDefault(p => p.AutoConnect) is { } auto)
            {
                SelectedProfile = auto;
                try
                {
                    await _session.AutoConnectAsync(auto);
                }
                catch (Exception ex)
                {
                    ErrorText = ex.Message;
                }
            }
        }
    }

    async partial void OnSelectedProfileChanged(VpnProfile? value)
    {
        Username = value?.Username ?? "";
        Password = "";
        SavePassword = value?.AuthMode == AuthMode.SaveLogin;
        if (value is null)
        {
            return;
        }

        Preferences.Default.Set(LastProfileKey, value.Id.ToString());
        try
        {
            Password = await _session.GetSavedPasswordAsync(value) ?? "";
        }
        catch (Exception ex)
        {
            // async void handler: an exception here would crash the app.
            ErrorText = $"Could not read the saved password: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        ErrorText = null;
        try
        {
            await _session.ConnectAsync(SelectedProfile!, Username, Password, SavePassword);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
    }

    private bool CanConnect() => SelectedProfile is not null && !IsBusy && !IsConnected;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private Task DisconnectAsync() => _session.DisconnectAsync();

    private bool CanDisconnect() => IsConnected || IsBusy || IsReconnecting;

    /// <summary>The "≡" menu next to the VPN name.</summary>
    [RelayCommand]
    private async Task ManageConnectionsAsync()
    {
        var options = SelectedProfile is null ? new[] { AddOption } : [AddOption, EditOption, DeleteOption];
        switch (await _dialogs.ChooseAsync("Connections", options))
        {
            case AddOption:
                await Shell.Current.GoToAsync(nameof(ConnectionEditPage));
                break;

            case EditOption:
                await Shell.Current.GoToAsync(nameof(ConnectionEditPage), new Dictionary<string, object>
                {
                    [ConnectionEditViewModel.ProfileIdKey] = SelectedProfile!.Id,
                });
                break;

            case DeleteOption when await _dialogs.ConfirmAsync(
                "Delete connection", $"Delete \"{SelectedProfile!.Name}\" and its saved password?", "Delete"):
                await _session.DeleteProfileAsync(SelectedProfile.Id);
                SelectedProfile = null;
                await LoadAsync();
                break;
        }
    }

    private void ApplyState(VpnState state)
    {
        IsBusy = state.IsBusy;
        IsConnected = state.Status == VpnStatus.Connected;
        IsReconnecting = _session.IsReconnecting;
        ErrorText = state.Error?.Message;
        UpdateCountdown();
        if (_session.PendingReconnect is { } pending && !IsBusy && !IsConnected)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling((pending.At - DateTimeOffset.UtcNow).TotalSeconds));
            StatusText = $"Reconnecting in {seconds} s (attempt {pending.Attempt})…";
            return;
        }

        StatusText = state.Status switch
        {
            VpnStatus.Connecting => $"Connecting to {state.Gateway?.Host}…",
            VpnStatus.Authenticating => $"Logging in to {state.Gateway?.Host}…",
            VpnStatus.Connected => $"Connected to {state.Gateway?.Host}",
            VpnStatus.Disconnecting => "Disconnecting…",
            VpnStatus.Error => "Not connected",
            _ => "Not connected",
        };
    }

    /// <summary>Ticks once a second while a reconnect waits, so the countdown moves.</summary>
    private void UpdateCountdown()
    {
        if (_session.PendingReconnect is null)
        {
            _countdown?.Stop();
            return;
        }

        if (_countdown is null)
        {
            _countdown = Application.Current!.Dispatcher.CreateTimer();
            _countdown.Interval = TimeSpan.FromSeconds(1);
            _countdown.Tick += (_, _) => ApplyState(_session.State);
        }

        if (!_countdown.IsRunning)
        {
            _countdown.Start();
        }
    }
}
