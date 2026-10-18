namespace OpenFortiClient.Core.Engine;

public enum VpnStatus
{
    Disconnected,
    Connecting,
    Authenticating,
    Connected,
    Disconnecting,
    Error,
}

public enum VpnErrorKind
{
    Unknown,
    InvalidProfile,
    EngineNotFound,
    ElevationFailed,
    GatewayUnreachable,
    UntrustedCertificate,
    AuthenticationFailed,
    OtpRequired,
    TunnelFailed,
}

/// <summary>Why a connection failed, in words the UI can show.</summary>
/// <param name="CertificateDigest">Set for <see cref="VpnErrorKind.UntrustedCertificate"/>, so the UI can offer to trust it.</param>
public sealed record VpnError(VpnErrorKind Kind, string Message, string? CertificateDigest = null);

/// <summary>A gateway that could not be reached or set up, and the one tried next (null after the last).</summary>
public sealed record GatewayFailure(Uri Gateway, VpnError Error, Uri? Next);

public sealed record VpnState(VpnStatus Status, Uri? Gateway = null, VpnError? Error = null)
{
    public static readonly VpnState Disconnected = new(VpnStatus.Disconnected);

    public bool IsBusy => Status is VpnStatus.Connecting or VpnStatus.Authenticating or VpnStatus.Disconnecting;
}
