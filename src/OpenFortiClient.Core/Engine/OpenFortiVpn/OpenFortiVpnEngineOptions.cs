using OpenFortiClient.Core.Settings;

namespace OpenFortiClient.Core.Engine.OpenFortiVpn;

public sealed record OpenFortiVpnEngineOptions
{
    public required string ExecutablePath { get; init; }

    /// <summary>How long to wait for "Connected to gateway." before trying the next gateway.</summary>
    public TimeSpan GatewayTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long to wait, once the gateway has answered, for "Tunnel is up and running." before giving up on
    /// this gateway. Paused while the user types a one-time password.
    /// </summary>
    public TimeSpan TunnelSetupTimeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>How long to wait for openfortivpn to clean up after SIGTERM.</summary>
    public TimeSpan DisconnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public bool SetDns { get; init; } = true;

    public bool SetRoutes { get; init; } = true;

    public static OpenFortiVpnEngineOptions From(AppSettings settings, AppPaths paths) => new()
    {
        ExecutablePath = settings.OpenFortiVpnPath,
        GatewayTimeout = TimeSpan.FromSeconds(settings.GatewayTimeoutSeconds),
        SetDns = settings.SetDns,
        SetRoutes = settings.SetRoutes,
    };
}
