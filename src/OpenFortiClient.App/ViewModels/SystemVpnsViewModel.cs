using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenFortiClient.App.Services;
using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.App.ViewModels;

/// <summary>
/// The "macOS VPN" card on the Connect page: one row per VPN service set up in System Settings.
/// Hidden when there are none (and always when not on macOS).
/// </summary>
public sealed partial class SystemVpnsViewModel : ObservableObject
{
    private readonly SystemVpnSession _session;
    private IDispatcherTimer? _countdown;

    public SystemVpnsViewModel(SystemVpnSession session)
    {
        _session = session;
        _session.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(Refresh);
        Refresh();
    }

    public ObservableCollection<SystemVpnRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial bool HasServices { get; set; }

    public void Refresh()
    {
        var items = _session.Items;
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (items.All(item => item.Service.Id != Rows[i].Id))
            {
                Rows.RemoveAt(i);
            }
        }

        foreach (var item in items)
        {
            var row = Rows.FirstOrDefault(r => r.Id == item.Service.Id);
            if (row is null)
            {
                row = new SystemVpnRowViewModel(_session, item.Service);
                Rows.Add(row);
            }

            row.Apply(item);
        }

        HasServices = Rows.Count > 0;
        UpdateCountdown(items.Any(i => i.PendingReconnect is not null));
    }

    /// <summary>Ticks once a second while a reconnect waits, so the countdown moves.</summary>
    private void UpdateCountdown(bool waiting)
    {
        if (!waiting)
        {
            _countdown?.Stop();
            return;
        }

        if (_countdown is null)
        {
            _countdown = Application.Current!.Dispatcher.CreateTimer();
            _countdown.Interval = TimeSpan.FromSeconds(1);
            _countdown.Tick += (_, _) => Refresh();
        }

        if (!_countdown.IsRunning)
        {
            _countdown.Start();
        }
    }
}

/// <summary>One macOS VPN service on the card: status, the On switch and the Keep it up switch.</summary>
public sealed partial class SystemVpnRowViewModel(SystemVpnSession session, SystemVpnService service) : ObservableObject
{
    // Set while the row shows a new state, so the switches' change handlers know it was not the user.
    private bool _applying;

    public string Id => service.Id;

    public string Name => service.Name;

    public string Kind => service.Kind == "IPSec" ? "Cisco IPSec" : service.Kind;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Not connected";

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    public partial bool KeepUp { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    internal void Apply(SystemVpnItem item)
    {
        _applying = true;
        try
        {
            var current = item.Current;
            IsOn = current.Status is SystemVpnStatus.Connecting or SystemVpnStatus.Connected || item.PendingReconnect is not null;
            KeepUp = item.KeepUp;
            ErrorText = item.State.Status == VpnStatus.Error ? item.State.Error?.Message : null;
            StatusText = Describe(current, item.PendingReconnect);
        }
        finally
        {
            _applying = false;
        }
    }

    internal static string Describe(SystemVpnState current, ReconnectSchedule? pending)
    {
        if (pending is not null && current.Status != SystemVpnStatus.Connected)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling((pending.At - DateTimeOffset.UtcNow).TotalSeconds));
            return $"Reconnecting in {seconds} s (attempt {pending.Attempt})…";
        }

        return current.Status switch
        {
            SystemVpnStatus.Connected => "Connected"
                + (current.Since is { } since ? $" since {since.ToLocalTime():HH:mm}" : "")
                + (current.Address is { } address ? $" · {address}" : ""),
            SystemVpnStatus.Connecting => "Connecting…",
            SystemVpnStatus.Disconnecting => "Disconnecting…",
            _ => "Not connected",
        };
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_applying)
        {
            _ = session.SetOnAsync(Id, value);
        }
    }

    partial void OnKeepUpChanged(bool value)
    {
        if (!_applying)
        {
            _ = session.SetKeepUpAsync(Id, value);
        }
    }
}
