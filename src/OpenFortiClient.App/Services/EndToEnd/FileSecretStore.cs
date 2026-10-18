#if E2E
using OpenFortiClient.Core.Secrets;

namespace OpenFortiClient.App.Services.EndToEnd;

/// <summary>Plain-text secrets for the end-to-end tests, so they never touch the Keychain.</summary>
public sealed class FileSecretStore(string directory) : ISecretStore
{
    private readonly string _file = Path.Combine(directory, "secrets.txt");
    private readonly Lock _lock = new();

    public Task<string?> GetAsync(string key)
    {
        lock (_lock)
        {
            return Task.FromResult(Read().GetValueOrDefault(key));
        }
    }

    public Task SetAsync(string key, string value)
    {
        lock (_lock)
        {
            var secrets = Read();
            secrets[key] = value;
            Write(secrets);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        lock (_lock)
        {
            var secrets = Read();
            secrets.Remove(key);
            Write(secrets);
        }

        return Task.CompletedTask;
    }

    // One "key<TAB>base64(value)" per line (a PEM spans lines); no JSON, so nothing depends on reflection.
    private Dictionary<string, string?> Read() =>
        File.Exists(_file)
            ? File.ReadAllLines(_file).Select(l => l.Split('\t', 2)).Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => (string?)System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p[1])))
            : [];

    private void Write(Dictionary<string, string?> secrets) =>
        File.WriteAllLines(_file, secrets.Select(s => $"{s.Key}\t{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s.Value ?? ""))}"));
}
#endif
