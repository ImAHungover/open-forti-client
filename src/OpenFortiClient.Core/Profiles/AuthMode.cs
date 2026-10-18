namespace OpenFortiClient.Core.Profiles;

/// <summary>How the user's password is handled, as on the FortiClient "Edit VPN Connection" screen.</summary>
public enum AuthMode
{
    /// <summary>Ask for the password on every connect.</summary>
    PromptOnLogin,

    /// <summary>Keep the password in the Keychain.</summary>
    SaveLogin,
}
