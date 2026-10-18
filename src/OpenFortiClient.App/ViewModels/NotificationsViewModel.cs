using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenFortiClient.Core.Notifications;

namespace OpenFortiClient.App.ViewModels;

/// <summary>Notification history (flows.md: Notification).</summary>
public sealed partial class NotificationsViewModel : ObservableObject
{
    private readonly INotificationHistory _history;

    public NotificationsViewModel(INotificationHistory history)
    {
        _history = history;
        _history.Added += (_, entry) => MainThread.BeginInvokeOnMainThread(() =>
        {
            // LoadAsync may already have read it from the file (Added is raised after the write).
            if (Entries.Contains(entry))
            {
                return;
            }

            Entries.Insert(0, entry);

            // Same limit as the stored history, so the page never shows rows that are already gone.
            while (Entries.Count > JsonNotificationHistory.DefaultMaxEntries)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }
        });
    }

    public ObservableCollection<NotificationEntry> Entries { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        Entries.Clear();
        foreach (var entry in await _history.GetAllAsync())
        {
            Entries.Add(entry);
        }
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        await _history.ClearAsync();
        Entries.Clear();
    }
}
