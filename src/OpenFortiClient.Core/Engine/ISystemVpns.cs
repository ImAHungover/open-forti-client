namespace OpenFortiClient.Core.Engine;

/// <summary>A VPN service set up in macOS System Settings, for example the company L2TP VPN.</summary>
/// <param name="Id">The service ID macOS gives it (a GUID).</param>
/// <param name="Name">Its name in System Settings, for example "VPN".</param>
/// <param name="Kind">"L2TP", "IPSec" (Cisco IPSec) or "PPTP".</param>
public sealed record SystemVpnService(string Id, string Name, string Kind);

public enum SystemVpnStatus
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
}

/// <summary>What macOS reports for a VPN service.</summary>
/// <param name="Since">When it connected (only while connected).</param>
/// <param name="Address">Its IPv4 address (only while connected).</param>
/// <param name="LastCause">
/// Why it last disconnected, as macOS reports it (PPP's or IPSec's LastCause); null if not reported.
/// <see cref="SystemVpnCause"/> reads it.
/// </param>
public sealed record SystemVpnState(
    SystemVpnStatus Status,
    DateTimeOffset? Since = null,
    string? Address = null,
    int? LastCause = null)
{
    public static SystemVpnState Disconnected { get; } = new(SystemVpnStatus.Disconnected);
}

/// <summary>A service's state changed.</summary>
public sealed record SystemVpnChange(string Id, SystemVpnState State);

/// <summary>
/// The VPN services set up in macOS System Settings, which the app shows and switches on and off.
/// macOS only: elsewhere <see cref="IsAvailable"/> is false and there are no services.
/// See docs/architecture.md, "macOS VPN services".
/// </summary>
public interface ISystemVpns
{
    /// <summary>The Mac's VPN services can be read and controlled here.</summary>
    bool IsAvailable { get; }

    /// <summary>The enabled VPN services that can be switched on and off.</summary>
    IReadOnlyList<SystemVpnService> Services();

    SystemVpnState State(string id);

    /// <summary>A service's state changed, also when changed in System Settings. Raised on a background thread.</summary>
    event EventHandler<SystemVpnChange>? Changed;

    /// <summary>Starts the service with the settings and credentials saved in macOS.</summary>
    /// <returns>Null if macOS started it; otherwise why not.</returns>
    Task<string?> StartAsync(string id);

    /// <returns>Null if macOS stopped it; otherwise why not.</returns>
    Task<string?> StopAsync(string id);
}

/// <summary>How a macOS VPN service went down, read from its LastCause.</summary>
public enum SystemVpnDisconnect
{
    /// <summary>Someone switched it off (in System Settings, or with <c>scutil --nc stop</c>).</summary>
    Stopped,

    /// <summary>The connection was lost or could not be made: worth trying again.</summary>
    Dropped,
}

/// <summary>
/// Reads macOS's LastCause for a VPN service. Measured on a real L2TP VPN (2026-10-21): a stop from the
/// app and a switch-off in System Settings both report 5, a connect reports 0; anything else is a drop.
/// No cause is known for a refused login, so that counts as a drop too (retried with backoff).
/// </summary>
public static class SystemVpnCause
{
    /// <summary>What macOS reports when someone switched the service off.</summary>
    public const int SwitchedOff = 5;

    public static SystemVpnDisconnect Classify(int? lastCause) => lastCause switch
    {
        null or 0 or SwitchedOff => SystemVpnDisconnect.Stopped,
        _ => SystemVpnDisconnect.Dropped,
    };
}
