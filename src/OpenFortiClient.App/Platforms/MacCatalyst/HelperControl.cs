using Foundation;
using ObjCRuntime;
using OpenFortiClient.App.Services;
using OpenFortiClient.Core.Logging;

namespace OpenFortiClient.App;

/// <summary>SMAppService for the helper, through OFCHelperControl in the native library.</summary>
public sealed class HelperControl(IAppLog log) : IHelperControl
{
    private readonly Lazy<NSObject?> _native = new(() =>
    {
        var native = NativeBridge.Create("OFCHelperControl", out var error);
        if (native is null)
        {
            log.Info("helper", error ?? "The helper control did not load.");
        }

        return native;
    });

    public HelperStatus Status
    {
        get
        {
            if (_native.Value is not { } native)
            {
                return HelperStatus.Unavailable;
            }

            var raw = NativeBridge.CallForString(native, "status");
            log.Debug("helper", $"SMAppService status: {raw}");
            return raw switch
            {
                "enabled" => HelperStatus.Enabled,
                "requiresApproval" => HelperStatus.RequiresApproval,

                // Before the first register(), SMAppService usually answers "not found" for a daemon.
                _ => HelperStatus.NotRegistered,
            };
        }
    }

    public string? Register() => Call("register");

    public string? Unregister() => Call("unregister");

    public void OpenLoginItemsSettings()
    {
        if (_native.Value is { } native)
        {
            NativeBridge.Call(native, "openLoginItemsSettings");
        }
    }

    private string? Call(string selector)
    {
        if (_native.Value is not { } native)
        {
            return "The privileged helper is not available in this build.";
        }

        var error = NativeBridge.CallForString(native, selector);
        log.Info("helper", $"{selector}: {error ?? "ok"}");
        return error;
    }
}
