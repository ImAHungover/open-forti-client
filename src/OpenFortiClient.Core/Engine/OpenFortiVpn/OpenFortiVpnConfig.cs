using System.Globalization;
using System.Text;
using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.Core.Engine.OpenFortiVpn;

/// <summary>
/// The openfortivpn config (<c>openfortivpn -c &lt;file&gt;</c>), as text: the launcher decides where it
/// is written. The password goes here and not on the command line, where any user could read it with <c>ps</c>.
/// </summary>
public sealed record OpenFortiVpnConfig
{
    public required string Host { get; init; }

    public int Port { get; init; } = GatewayAddress.DefaultPort;

    public string? Username { get; init; }

    public string? Password { get; init; }

    public IReadOnlyList<string> TrustedCertDigests { get; init; } = [];

    public string? UserCertPath { get; init; }

    public string? UserKeyPath { get; init; }

    public bool SetDns { get; init; } = true;

    public bool SetRoutes { get; init; } = true;

    public static OpenFortiVpnConfig Create(
        VpnProfile profile, Uri gateway, VpnCredentials credentials, OpenFortiVpnEngineOptions options) => new()
    {
        Host = gateway.Host,
        Port = gateway.Port,
        Username = string.IsNullOrWhiteSpace(credentials.Username) ? profile.Username : credentials.Username,
        Password = credentials.Password,
        TrustedCertDigests = profile.TrustedCertDigests,
        UserCertPath = profile.ClientCertificatePath,
        UserKeyPath = profile.ClientKeyPath,
        SetDns = options.SetDns,
        SetRoutes = options.SetRoutes,
    };

    public string Render()
    {
        var builder = new StringBuilder();
        Append(builder, "host", Host);
        Append(builder, "port", Port.ToString(CultureInfo.InvariantCulture));
        Append(builder, "username", Username);
        // An empty password line would make openfortivpn prompt for one; leave it out instead.
        Append(builder, "password", string.IsNullOrEmpty(Password) ? null : Password);
        foreach (var digest in TrustedCertDigests)
        {
            Append(builder, "trusted-cert", digest);
        }

        Append(builder, "user-cert", UserCertPath);
        Append(builder, "user-key", UserKeyPath);
        Append(builder, "set-dns", SetDns ? "1" : "0");
        Append(builder, "set-routes", SetRoutes ? "1" : "0");
        return builder.ToString();
    }

    // Records print every property by default; never print the password.
    public override string ToString() => $"OpenFortiVpnConfig {{ Host = {Host}, Port = {Port}, Username = {Username} }}";

    private static void Append(StringBuilder builder, string key, string? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.AsSpan().ContainsAny('\r', '\n'))
        {
            throw new ArgumentException($"The {key} must not contain line breaks.", key);
        }

        builder.Append(key).Append(" = ").Append(value).Append('\n');
    }
}
