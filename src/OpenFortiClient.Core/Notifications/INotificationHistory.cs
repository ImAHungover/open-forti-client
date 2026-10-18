namespace OpenFortiClient.Core.Notifications;

public interface INotificationHistory
{
    event EventHandler<NotificationEntry>? Added;

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<NotificationEntry>> GetAllAsync(CancellationToken ct = default);

    Task AddAsync(NotificationEntry entry, CancellationToken ct = default);

    Task ClearAsync(CancellationToken ct = default);
}
