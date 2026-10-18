namespace OpenFortiClient.App.Services;

public enum HelperStatus
{
    /// <summary>The helper is not part of this build, or the native library did not load.</summary>
    Unavailable,
    NotRegistered,

    /// <summary>Registered, waiting for the user in System Settings → General → Login Items.</summary>
    RequiresApproval,
    Enabled,
}

/// <summary>Installs and removes the privileged helper (SMAppService). See docs/architecture.md, "Elevation".</summary>
public interface IHelperControl
{
    HelperStatus Status { get; }

    /// <returns>null when it worked, otherwise why not.</returns>
    string? Register();

    /// <returns>null when it worked, otherwise why not.</returns>
    string? Unregister();

    void OpenLoginItemsSettings();
}
