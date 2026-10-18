namespace OpenFortiClient.Core.Settings;

public enum AppLogLevel
{
    Information,
    Debug,
}

/// <summary>App-wide settings stored in <c>settings.json</c>.</summary>
public sealed record AppSettings
{
    // Properties use set, not init: the source-generated JSON reader fills init properties like
    // constructor arguments, so a property missing from the file got default(T) instead of the value
    // below (a missing WarnInvalidServerCertificate became false). Covered by the JsonSettingsStore
    // and VpnProfile tests.

    public AppLogLevel LogLevel { get; set; } = AppLogLevel.Information;

    /// <summary>
    /// Ask before trusting an unknown gateway certificate.
    /// Mirrors FortiClient's "Do not warn invalid server certificate" (inverted).
    /// </summary>
    public bool WarnInvalidServerCertificate { get; set; } = true;

    /// <summary>Path to the openfortivpn binary (Homebrew on Apple Silicon by default).</summary>
    public string OpenFortiVpnPath { get; set; } = "/opt/homebrew/bin/openfortivpn";

    /// <summary>How long to wait for a gateway before trying the next one.</summary>
    public int GatewayTimeoutSeconds { get; set; } = 30;

    /// <summary>Let openfortivpn add the VPN's DNS servers.</summary>
    public bool SetDns { get; set; } = true;

    /// <summary>Let openfortivpn add the routes pushed by the gateway.</summary>
    public bool SetRoutes { get; set; } = true;

    /// <summary>Show macOS banners for connected, lost, failed and failover (always recorded in the history).</summary>
    public bool ShowSystemNotifications { get; set; } = true;

    /// <summary>
    /// Development builds only: use the privileged helper instead of sudo. Release builds always use the helper.
    /// </summary>
    public bool UsePrivilegedHelper { get; set; }

    /// <summary>
    /// Waits before the reconnect attempts of an always-up connection, in seconds; the last one repeats.
    /// Not on the Settings page; null means 5, 10, 30, 60. The UI tests set short ones.
    /// </summary>
    public List<int>? ReconnectDelaysSeconds { get; set; }
}
