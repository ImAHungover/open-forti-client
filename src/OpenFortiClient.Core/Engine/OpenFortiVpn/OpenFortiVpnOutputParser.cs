using System.Text.RegularExpressions;

namespace OpenFortiClient.Core.Engine.OpenFortiVpn;

public enum OutputEventKind
{
    ConnectedToGateway,
    Authenticated,
    TunnelUp,
    CertificateUntrusted,
    TrustedCertDigest,
    OtpPrompt,
    PasswordPrompt,
    AuthenticationFailed,
    GatewayUnreachable,
    TunnelSetupFailed,
    ElevationFailed,

    /// <summary>The tunnel is going down. openfortivpn may still run for a while after this, trying to log out.</summary>
    TunnelClosed,
}

public readonly record struct OutputEvent(OutputEventKind Kind, string? Value = null);

/// <summary>
/// Turns openfortivpn output lines into events. The strings were taken from openfortivpn 1.24.1;
/// keep this the only place that knows them.
/// </summary>
public static partial class OpenFortiVpnOutputParser
{
    public const string OtpPromptText = "Two-factor authentication token:";
    public const string OtpPromptAltText = "Please enter one-time password:";
    public const string PasswordPromptText = "VPN account password:";

    /// <summary>Prompts openfortivpn prints without a newline.</summary>
    public static IReadOnlyList<string> PromptMarkers { get; } = [OtpPromptText, OtpPromptAltText, PasswordPromptText];

    private static readonly (string Text, OutputEventKind Kind)[] KnownMessages =
    [
        ("Tunnel is up and running.", OutputEventKind.TunnelUp),
        ("Connected to gateway.", OutputEventKind.ConnectedToGateway),
        ("Authenticated.", OutputEventKind.Authenticated),
        ("Gateway certificate validation failed, and the certificate digest is not in the local whitelist", OutputEventKind.CertificateUntrusted),
        (OtpPromptText, OutputEventKind.OtpPrompt),
        (OtpPromptAltText, OutputEventKind.OtpPrompt),
        (PasswordPromptText, OutputEventKind.PasswordPrompt),
        ("Could not authenticate to gateway", OutputEventKind.AuthenticationFailed),
        ("Could not authenticate to the gateway", OutputEventKind.AuthenticationFailed),
        ("Could not get route to gateway", OutputEventKind.GatewayUnreachable),
        ("getaddrinfo:", OutputEventKind.GatewayUnreachable),
        ("connect: ", OutputEventKind.GatewayUnreachable),
        ("SSL_connect:", OutputEventKind.GatewayUnreachable),
        ("Could not get VPN configuration", OutputEventKind.TunnelSetupFailed),
        ("Could not start tunnel", OutputEventKind.TunnelSetupFailed),
        ("Cancelling threads...", OutputEventKind.TunnelClosed),
        ("Closed connection to gateway.", OutputEventKind.TunnelClosed),
        ("Logged out.", OutputEventKind.TunnelClosed),
        ("sudo: a password is required", OutputEventKind.ElevationFailed),
        ("is not in the sudoers file", OutputEventKind.ElevationFailed),
        ("is not allowed to execute", OutputEventKind.ElevationFailed),
        ("not spawned with root privileges", OutputEventKind.ElevationFailed),
    ];

    public static OutputEvent? Parse(string line)
    {
        // The digest follows the certificate error as "    --trusted-cert <sha256>".
        var digest = TrustedCertDigest().Match(line);
        if (digest.Success)
        {
            return new OutputEvent(OutputEventKind.TrustedCertDigest, digest.Groups["digest"].Value.ToLowerInvariant());
        }

        foreach (var (text, kind) in KnownMessages)
        {
            if (line.Contains(text, StringComparison.Ordinal))
            {
                return new OutputEvent(kind, StripLevel(line));
            }
        }

        return null;
    }

    /// <summary>"ERROR:  connect: Operation timed out" becomes "connect: Operation timed out".</summary>
    private static string StripLevel(string line) => LogLevelPrefix().Replace(line, string.Empty).Trim();

    [GeneratedRegex("""--trusted-cert[ =]+(?<digest>[0-9a-fA-F]{64})\b""")]
    private static partial Regex TrustedCertDigest();

    [GeneratedRegex("""^\s*(?:ERROR|WARN|INFO|DEBUG):\s*""")]
    private static partial Regex LogLevelPrefix();
}
