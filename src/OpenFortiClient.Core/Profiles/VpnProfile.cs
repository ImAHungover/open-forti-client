namespace OpenFortiClient.Core.Profiles;

/// <summary>
/// One saved VPN connection. Mirrors the FortiClient "Edit VPN Connection" screen.
/// Secrets are never stored here; they live in the <see cref="Secrets.ISecretStore"/>.
/// </summary>
public sealed record VpnProfile
{
    // Properties use set, not init: the source-generated JSON reader fills init properties like
    // constructor arguments, so a property missing from the file got default(T) instead of the value
    // below (a missing WarnInvalidServerCertificate became false). Covered by the JsonSettingsStore
    // and VpnProfile tests.

    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Gateways tried in order until one connects.</summary>
    public IReadOnlyList<Uri> Gateways { get; set; } = [];

    public AuthMode AuthMode { get; set; } = AuthMode.PromptOnLogin;

    public string? Username { get; set; }

    /// <summary>PEM client certificate passed to openfortivpn as <c>user-cert</c>.</summary>
    public string? ClientCertificatePath { get; set; }

    /// <summary>PEM private key passed to openfortivpn as <c>user-key</c>.</summary>
    public string? ClientKeyPath { get; set; }

    /// <summary>SHA-256 digests of gateway certificates the user chose to trust.</summary>
    public IReadOnlyList<string> TrustedCertDigests { get; set; } = [];

    /// <summary>The client private key is in the Keychain (<see cref="Secrets.SecretKeys.ClientKey"/>), not at <see cref="ClientKeyPath"/>.</summary>
    public bool ClientKeyInKeychain { get; set; }

    public bool AutoConnect { get; set; }

    public bool AlwaysUp { get; set; }

    /// <summary>Returns human-readable problems, or an empty list if the profile is usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Connection name is required.");
        }

        if (Gateways.Count == 0)
        {
            errors.Add("At least one remote gateway is required.");
        }

        if (Gateways.Any(g => g.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("Remote gateways must use https.");
        }

        if (AuthMode == AuthMode.SaveLogin && string.IsNullOrWhiteSpace(Username))
        {
            errors.Add("Username is required when the login is saved.");
        }

        var hasKey = ClientKeyPath is not null || ClientKeyInKeychain;
        if (ClientCertificatePath is null == hasKey)
        {
            errors.Add("Client certificate and key must be set together.");
        }

        return errors;
    }
}
