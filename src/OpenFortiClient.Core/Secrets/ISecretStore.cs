namespace OpenFortiClient.Core.Secrets;

/// <summary>
/// Stores secrets outside of the profile files. On macOS this is the Keychain.
/// </summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    Task RemoveAsync(string key);
}

public static class SecretKeys
{
    public static string Password(Guid profileId) => $"profile/{profileId:N}/password";

    /// <summary>The client private key (PEM) of a profile with <c>ClientKeyInKeychain</c>.</summary>
    public static string ClientKey(Guid profileId) => $"profile/{profileId:N}/client-key";
}
