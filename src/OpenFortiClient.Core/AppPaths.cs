namespace OpenFortiClient.Core;

/// <summary>
/// Locations of the files the app reads and writes for the current user.
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "OpenFortiClient";

    public AppPaths(string dataDirectory, string logDirectory)
    {
        DataDirectory = dataDirectory;
        LogDirectory = logDirectory;
    }

    /// <summary>~/Library/Application Support/OpenFortiClient</summary>
    public string DataDirectory { get; }

    /// <summary>~/Library/Logs/OpenFortiClient</summary>
    public string LogDirectory { get; }

    public string ProfilesFile => Path.Combine(DataDirectory, "profiles.json");

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public string NotificationsFile => Path.Combine(DataDirectory, "notifications.json");

    /// <summary>Private folder (0700) for the short-lived openfortivpn config files.</summary>
    public string RuntimeDirectory => Path.Combine(DataDirectory, "run");

    public static AppPaths ForCurrentUser()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var library = Path.Combine(home, "Library");
        return new AppPaths(
            Path.Combine(library, "Application Support", AppFolderName),
            Path.Combine(library, "Logs", AppFolderName));
    }
}
