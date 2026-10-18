using System.IO.Compression;
using OpenFortiClient.Core.Settings;

namespace OpenFortiClient.Core.Logging;

/// <summary>
/// Writes sanitised log lines to <c>~/Library/Logs/OpenFortiClient/app.log</c>,
/// rolling over to <c>app.1.log</c> when the file gets too big.
/// </summary>
public sealed class FileAppLog(AppPaths paths, long maxFileBytes = 2 * 1024 * 1024) : IAppLog
{
    private readonly Lock _lock = new();

    public AppLogLevel MinimumLevel { get; set; } = AppLogLevel.Information;

    private string CurrentFile => Path.Combine(paths.LogDirectory, "app.log");

    private string PreviousFile => Path.Combine(paths.LogDirectory, "app.1.log");

    public void Write(AppLogLevel level, string source, string message)
    {
        if (level > MinimumLevel)
        {
            return;
        }

        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {source}: {LogSanitizer.Sanitize(message)}";

        lock (_lock)
        {
            PrivateFile.EnsureDirectory(paths.LogDirectory);
            RollIfNeeded();
            File.AppendAllText(CurrentFile, line + Environment.NewLine);
        }
    }

    /// <summary>Zips the log folder into <paramref name="zipPath"/> for "Export logs".</summary>
    public void Export(string zipPath)
    {
        lock (_lock)
        {
            PrivateFile.EnsureDirectory(paths.LogDirectory);
            File.Delete(zipPath);
            ZipFile.CreateFromDirectory(paths.LogDirectory, zipPath);
        }
    }

    /// <summary>Deletes all log files for "Clear logs".</summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (!Directory.Exists(paths.LogDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(paths.LogDirectory, "*.log"))
            {
                File.Delete(file);
            }
        }
    }

    private void RollIfNeeded()
    {
        var current = new FileInfo(CurrentFile);
        if (current.Exists && current.Length >= maxFileBytes)
        {
            File.Move(CurrentFile, PreviousFile, overwrite: true);
        }
    }
}
