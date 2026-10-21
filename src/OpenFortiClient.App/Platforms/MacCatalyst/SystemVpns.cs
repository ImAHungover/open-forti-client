using Foundation;
using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Logging;

namespace OpenFortiClient.App;

/// <summary>
/// The VPN services set up in macOS System Settings, through OFCSystemVpns in the native library, which
/// calls back with <c>systemVpnChanged:</c>. Unavailable when the library did not load.
/// See docs/architecture.md, "macOS VPN services".
/// </summary>
public sealed class SystemVpns : NSObject, ISystemVpns
{
    private const string LogSource = "macos-vpn";
    private readonly IAppLog _log;
    private readonly Lazy<NSObject?> _native;

    public SystemVpns(IAppLog log)
    {
        _log = log;
        _native = new Lazy<NSObject?>(() =>
        {
            var native = NativeBridge.Create("OFCSystemVpns", out var error);
            if (native is null)
            {
                log.Info(LogSource, error ?? "The macOS VPN services did not load.");
            }
            else
            {
                NativeBridge.Call(native, "setDelegate:", this);
            }

            return native;
        });
    }

    public bool IsAvailable => _native.Value is not null;

    public event EventHandler<SystemVpnChange>? Changed;

    public IReadOnlyList<SystemVpnService> Services()
    {
        if (_native.Value is not { } native || NativeBridge.CallForObject<NSArray>(native, "services") is not { } array)
        {
            return [];
        }

        var services = new List<SystemVpnService>();
        for (nuint i = 0; i < array.Count; i++)
        {
            if (array.GetItem<NSDictionary>(i) is { } info && Text(info, "id") is { } id)
            {
                services.Add(new SystemVpnService(id, Text(info, "name") ?? id, Text(info, "kind") ?? "VPN"));
            }
        }

        return services;
    }

    public SystemVpnState State(string id)
    {
        if (_native.Value is not { } native)
        {
            return SystemVpnState.Disconnected;
        }

        using var argument = new NSString(id);
        return NativeBridge.CallForObject<NSDictionary>(native, "status:", argument) is { } info
            ? Parse(info)
            : SystemVpnState.Disconnected;
    }

    public Task<string?> StartAsync(string id) => Task.Run(() => Call("start:", id));

    public Task<string?> StopAsync(string id) => Task.Run(() => Call("stop:", id));

    /// <summary>From OFCSystemVpns, on its own queue.</summary>
    [Export("systemVpnChanged:")]
    public void SystemVpnChanged(NSDictionary info)
    {
        if (Text(info, "id") is not { } id)
        {
            return;
        }

        var state = Parse(info);
        _log.Info(LogSource, $"{id}: {state.Status}{(state.LastCause is { } cause ? $", last cause {cause}" : "")}");
        Changed?.Invoke(this, new SystemVpnChange(id, state));
    }

    private string? Call(string selector, string id)
    {
        if (_native.Value is not { } native)
        {
            return "macOS VPN services are not available in this build.";
        }

        using var argument = new NSString(id);
        var error = NativeBridge.CallForString(native, selector, argument);
        _log.Info(LogSource, $"{selector.TrimEnd(':')} {id}: {error ?? "ok"}");
        return error;
    }

    private static SystemVpnState Parse(NSDictionary info) => new(
        Text(info, "status") switch
        {
            "connected" => SystemVpnStatus.Connected,
            "connecting" => SystemVpnStatus.Connecting,
            "disconnecting" => SystemVpnStatus.Disconnecting,
            _ => SystemVpnStatus.Disconnected,
        },
        info["since"] is NSNumber since ? DateTimeOffset.FromUnixTimeMilliseconds((long)(since.DoubleValue * 1000)) : null,
        Text(info, "address"),
        info["lastCause"] is NSNumber lastCause ? lastCause.Int32Value : null);

    private static string? Text(NSDictionary info, string key) => info[key]?.ToString();
}
