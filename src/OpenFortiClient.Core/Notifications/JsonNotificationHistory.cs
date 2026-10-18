namespace OpenFortiClient.Core.Notifications;

/// <summary>Keeps the most recent notifications in <c>notifications.json</c>.</summary>
public sealed class JsonNotificationHistory(AppPaths paths, int maxEntries = JsonNotificationHistory.DefaultMaxEntries) : INotificationHistory
{
    /// <summary>How many entries are kept; older ones are dropped.</summary>
    public const int DefaultMaxEntries = 200;

    private readonly SemaphoreSlim _lock = new(1, 1);

    public event EventHandler<NotificationEntry>? Added;

    public async Task<IReadOnlyList<NotificationEntry>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await LoadAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddAsync(NotificationEntry entry, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var entries = await LoadAsync(ct);
            entries.Insert(0, entry);
            if (entries.Count > maxEntries)
            {
                entries.RemoveRange(maxEntries, entries.Count - maxEntries);
            }

            await SaveAsync(entries, ct);
        }
        finally
        {
            _lock.Release();
        }

        Added?.Invoke(this, entry);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await SaveAsync([], ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<NotificationEntry>> LoadAsync(CancellationToken ct) =>
        await JsonFile.ReadAsync(paths.NotificationsFile, CoreJsonContext.Default.ListNotificationEntry, ct) ?? [];

    private Task SaveAsync(List<NotificationEntry> entries, CancellationToken ct) =>
        JsonFile.WriteAsync(paths.NotificationsFile, entries, CoreJsonContext.Default.ListNotificationEntry, ct);
}
