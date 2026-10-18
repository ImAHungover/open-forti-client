using OpenFortiClient.Core.Logging;

namespace OpenFortiClient.Core.Engine;

/// <summary>What the user typed (or the Keychain returned) for one connect attempt.</summary>
public sealed record VpnCredentials(string? Username, string? Password)
{
    /// <summary>Called when the gateway asks for a one-time password. Return null to cancel.</summary>
    public Func<CancellationToken, Task<string?>>? OtpProvider { get; init; }

    /// <summary>The client private key (PEM) from the Keychain, for profiles that keep it there.</summary>
    public string? ClientKeyPem { get; init; }

    // Records print every property by default; never print the password.
    public override string ToString() =>
        $"VpnCredentials {{ Username = {Username}, Password = {(Password is null ? "<none>" : LogSanitizer.Mask)}, ClientKey = {(ClientKeyPem is null ? "<none>" : LogSanitizer.Mask)} }}";
}
