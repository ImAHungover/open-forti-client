namespace OpenFortiClient.Core.Engine;

/// <summary>
/// The editable settings of a macOS L2TP VPN service, as System Settings shows them. The password and the
/// shared secret are not here: they are never read, only replaced (<see cref="SystemVpnSettingsChange"/>).
/// </summary>
/// <param name="Server">Server address (host name or IP address).</param>
/// <param name="Account">Account name.</param>
/// <param name="SendAllTraffic">"Send all traffic over VPN connection".</param>
/// <param name="EchoIntervalSeconds">Seconds between PPP keep-alive echoes.</param>
/// <param name="EchoFailures">Unanswered echoes after which the connection counts as dead.</param>
public sealed record SystemVpnSettings(
    string Server,
    string Account,
    bool SendAllTraffic,
    int EchoIntervalSeconds,
    int EchoFailures)
{
    public const int MinEchoIntervalSeconds = 5;
    public const int MaxEchoIntervalSeconds = 120;
    public const int MinEchoFailures = 1;
    public const int MaxEchoFailures = 30;

    /// <summary>How long a dead connection goes unnoticed at most.</summary>
    public int DeadAfterSeconds => EchoIntervalSeconds * EchoFailures;
}

/// <summary>What to save: the settings, and a new password or shared secret where the user typed one.</summary>
/// <param name="Password">A new password, or null to keep the saved one.</param>
/// <param name="SharedSecret">A new shared secret, or null to keep the saved one.</param>
public sealed record SystemVpnSettingsChange(SystemVpnSettings Settings, string? Password = null, string? SharedSecret = null)
{
    private const int MaxTextLength = 256;
    private const int MaxHostLength = 253;

    /// <summary>
    /// The same rules the privileged helper enforces (OpenFortiClient.Helper/VpnSettingsWriter.swift), checked
    /// here first so the user gets the message before anything is sent.
    /// </summary>
    /// <returns>Human-readable problems; empty if the change can be saved.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var settings = Settings;

        if (string.IsNullOrWhiteSpace(settings.Server))
        {
            errors.Add("Server address is required.");
        }
        else if (settings.Server.Length > MaxHostLength || !settings.Server.All(IsHostCharacter))
        {
            errors.Add("Server address must be a host name or an IP address.");
        }

        if (settings.Account.Length > MaxTextLength || settings.Account.Any(char.IsControl))
        {
            errors.Add("Account name is too long or contains control characters.");
        }

        if (settings.EchoIntervalSeconds is < SystemVpnSettings.MinEchoIntervalSeconds or > SystemVpnSettings.MaxEchoIntervalSeconds)
        {
            errors.Add($"Keep-alive interval must be {SystemVpnSettings.MinEchoIntervalSeconds} to {SystemVpnSettings.MaxEchoIntervalSeconds} seconds.");
        }

        if (settings.EchoFailures is < SystemVpnSettings.MinEchoFailures or > SystemVpnSettings.MaxEchoFailures)
        {
            errors.Add($"Keep-alive failures must be {SystemVpnSettings.MinEchoFailures} to {SystemVpnSettings.MaxEchoFailures}.");
        }

        if (!IsValidSecret(Password))
        {
            errors.Add("Password is too long or contains a NUL character.");
        }

        if (!IsValidSecret(SharedSecret))
        {
            errors.Add("Shared secret is too long or contains a NUL character.");
        }

        return errors;
    }

    private static bool IsHostCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':';

    private static bool IsValidSecret(string? secret) => secret is null || (secret.Length <= MaxTextLength && !secret.Contains('\0'));
}
