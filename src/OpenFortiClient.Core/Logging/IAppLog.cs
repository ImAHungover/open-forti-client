using OpenFortiClient.Core.Settings;

namespace OpenFortiClient.Core.Logging;

/// <summary>The app log shown in Settings and exported for support.</summary>
public interface IAppLog
{
    AppLogLevel MinimumLevel { get; set; }

    void Write(AppLogLevel level, string source, string message);
}

public static class AppLogExtensions
{
    public static void Info(this IAppLog log, string source, string message) =>
        log.Write(AppLogLevel.Information, source, message);

    public static void Debug(this IAppLog log, string source, string message) =>
        log.Write(AppLogLevel.Debug, source, message);
}
