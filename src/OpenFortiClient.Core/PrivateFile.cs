namespace OpenFortiClient.Core;

/// <summary>
/// File and folder options that keep data readable by the current user only.
/// </summary>
internal static class PrivateFile
{
    /// <summary>Create a new file with mode 0600.</summary>
    public static FileStreamOptions CreateOptions
    {
        get
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            return options;
        }
    }

    /// <summary>Create a folder with mode 0700, or tighten an existing one.</summary>
    public static void EnsureDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Directory.CreateDirectory(path, ownerOnly);
        File.SetUnixFileMode(path, ownerOnly);
    }
}
