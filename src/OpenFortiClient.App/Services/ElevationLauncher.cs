using OpenFortiClient.Core.Engine;
using OpenFortiClient.Core.Settings;

namespace OpenFortiClient.App.Services;

/// <summary>
/// Chooses how openfortivpn gets root: the privileged helper (release builds, or development builds with
/// "Use the privileged helper" on) or sudo (development builds). A helper that is not approved yet is
/// refused with the way to fix it, before anything starts.
/// </summary>
public sealed class ElevationLauncher(IPrivilegedLauncher sudo, IPrivilegedLauncher helper, IHelperControl control) : IPrivilegedLauncher
{
#if DEBUG || E2E
    /// <summary>From the settings; development builds (and the UI tests' copy) use sudo unless this is on.</summary>
    public bool UseHelper { get; set; }

    public static bool UsesHelper(AppSettings settings) => settings.UsePrivilegedHelper;
#else
    /// <summary>Release builds always use the helper.</summary>
    public bool UseHelper { get => true; set { } }

    public static bool UsesHelper(AppSettings settings) => true;
#endif

    /// <summary>
    /// The openfortivpn that is used: the one bundled in the app with the helper (the helper runs no other),
    /// the one from the settings with sudo.
    /// </summary>
    public static string OpenFortiVpnPath(AppSettings settings) =>
        UsesHelper(settings) ? BundledOpenFortiVpnPath : settings.OpenFortiVpnPath;

    /// <summary>Contents/MacOS/openfortivpn; the app's assemblies are in Contents/MonoBundle.</summary>
    public static string BundledOpenFortiVpnPath { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "MacOS", "openfortivpn"));

    public IPrivilegedProcess Start(LaunchRequest request)
    {
        if (!UseHelper)
        {
            return sudo.Start(request);
        }

        return control.Status switch
        {
            HelperStatus.Enabled => helper.Start(request),
            HelperStatus.RequiresApproval => throw new InvalidOperationException(
                "the privileged helper is waiting for your approval. Allow OpenFortiClient in System Settings → General → Login Items."),
            HelperStatus.NotRegistered => throw new InvalidOperationException(
                "the privileged helper is not installed. Install it under Settings → Privileged helper."),
            _ => throw new InvalidOperationException("the privileged helper is not available in this build."),
        };
    }
}
