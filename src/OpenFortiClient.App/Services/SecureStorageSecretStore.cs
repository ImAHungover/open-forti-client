using OpenFortiClient.Core.Secrets;

namespace OpenFortiClient.App.Services;

/// <summary>MAUI SecureStorage; on macOS this is the Keychain.</summary>
public sealed class SecureStorageSecretStore : ISecretStore
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public Task RemoveAsync(string key)
    {
        SecureStorage.Default.Remove(key);
        return Task.CompletedTask;
    }
}
