using System.Text;

namespace OpenFortiClient.Core.Engine;

/// <summary>
/// The config (and client key) of one launch as 0600 files in a 0700 folder, for launchers that have to
/// hand openfortivpn a file: <c>openfortivpn -c &lt;file&gt;</c>. openfortivpn reads the config before it
/// prints anything, so it is deleted at the first output line (<see cref="DeleteConfig"/>). The key is not:
/// openfortivpn loads it again for every TLS connection (login, configuration, tunnel), so it goes when the
/// process ends (<see cref="Dispose"/>).
/// </summary>
public sealed class LaunchFiles : IDisposable
{
    private LaunchFiles(string configPath, string? keyPath)
    {
        ConfigPath = configPath;
        KeyPath = keyPath;
    }

    public string ConfigPath { get; }

    public string? KeyPath { get; }

    public static LaunchFiles Write(LaunchRequest request, string directory)
    {
        PrivateFile.EnsureDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        var config = request.ConfigText;

        string? keyPath = null;
        if (request.ClientKeyPem is { } pem)
        {
            keyPath = Path.Combine(directory, $"{id}.key");
            WritePrivate(keyPath, pem);
            config = $"{config.TrimEnd('\n')}\nuser-key = {keyPath}\n";
        }

        var configPath = Path.Combine(directory, $"{id}.conf");
        WritePrivate(configPath, config);
        return new LaunchFiles(configPath, keyPath);
    }

    /// <summary>Removes the config (it holds the password); safe to call more than once.</summary>
    public void DeleteConfig() => DeleteQuietly(ConfigPath);

    /// <summary>Removes both files; safe to call more than once.</summary>
    public void Dispose()
    {
        DeleteConfig();
        if (KeyPath is not null)
        {
            DeleteQuietly(KeyPath);
        }
    }

    private static void WritePrivate(string path, string content)
    {
        using var stream = new FileStream(path, PrivateFile.CreateOptions);
        stream.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort; the folder is private and cleaned at the next start.
        }
    }
}
