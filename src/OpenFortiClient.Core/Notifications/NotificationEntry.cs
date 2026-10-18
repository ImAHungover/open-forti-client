namespace OpenFortiClient.Core.Notifications;

public enum NotificationSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>One row on the Notifications page.</summary>
public sealed record NotificationEntry(
    DateTimeOffset Time,
    string Source,
    string Message,
    NotificationSeverity Severity = NotificationSeverity.Info);
