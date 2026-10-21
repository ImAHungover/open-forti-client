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

    public SystemVpnSettings? Settings(string id)
    {
        if (_native.Value is not { } native)
        {
            return null;
        }

        using var argument = new NSString(id);
        if (NativeBridge.CallForObject<NSDictionary>(native, "settings:", argument) is not { } info)
        {
            return null;
        }

        return new SystemVpnSettings(
            Text(info, "server") ?? "",
            Text(info, "account") ?? "",
            info["sendAllTraffic"] is NSNumber all && all.BoolValue,
            info["echoInterval"] is NSNumber interval ? interval.Int32Value : 20,
            info["echoFailures"] is NSNumber failures ? failures.Int32Value : 15);
    }

    /// <summary>Through the privileged helper (root), with its own short connection.</summary>
    public Task<string?> SaveSettingsAsync(string id, SystemVpnSettingsChange change) => Task.Run(() =>
    {
        if (NativeBridge.Create("OFCHelperClient", out var error) is not { } client)
        {
            return error ?? "The privileged helper is not available in this build.";
        }

        var settings = change.Settings;
        var request = new NSMutableDictionary
        {
            ["serviceId"] = new NSString(id),
            ["server"] = new NSString(settings.Server),
            ["account"] = new NSString(settings.Account),
            ["sendAllTraffic"] = NSNumber.FromBoolean(settings.SendAllTraffic),
            ["echoInterval"] = NSNumber.FromInt32(settings.EchoIntervalSeconds),
            ["echoFailures"] = NSNumber.FromInt32(settings.EchoFailures),
        };
        if (change.Password is { } password)
        {
            request["password"] = new NSString(password);
        }

        if (change.SharedSecret is { } secret)
        {
            request["sharedSecret"] = new NSString(secret);
        }

        var failure = NativeBridge.CallForString(client, "writeVpnSettings:", request);

        // Never the values: the request holds secrets.
        _log.Info(LogSource, $"Save settings of {id}: {failure ?? "ok"}{(change.Password is null ? "" : ", new password")}{(change.SharedSecret is null ? "" : ", new shared secret")}");
        return failure;
    });

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
