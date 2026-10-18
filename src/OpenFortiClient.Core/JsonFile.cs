using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace OpenFortiClient.Core;

/// <summary>
/// Small helpers for reading and atomically writing JSON files.
/// </summary>
internal static class JsonFile
{
    public static async Task<T?> ReadAsync<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct);
    }

    /// <summary>
    /// Writes to a temp file next to the target, then moves it over the target,
    /// so a crash never leaves a half-written file behind.
    /// </summary>
    public static async Task WriteAsync<T>(string path, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, PrivateFile.CreateOptions))
            {
                await JsonSerializer.SerializeAsync(stream, value, typeInfo, ct);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
