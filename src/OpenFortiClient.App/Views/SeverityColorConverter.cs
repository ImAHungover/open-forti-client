using System.Globalization;
using OpenFortiClient.Core.Notifications;

namespace OpenFortiClient.App.Views;

/// <summary>Colour for a notification's severity: muted for info, amber for warnings, red for errors.</summary>
public sealed class SeverityColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            NotificationSeverity.Error => "Danger",
            NotificationSeverity.Warning => "Warning",
            _ => Application.Current?.RequestedTheme == AppTheme.Dark ? "MutedDark" : "MutedLight",
        };
        var color = Application.Current?.Resources.TryGetValue(key, out var found) == true ? found as Color : null;
        return targetType == typeof(Brush) ? new SolidColorBrush(color) : color;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
