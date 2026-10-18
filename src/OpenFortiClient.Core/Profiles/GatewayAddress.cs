using System.Diagnostics.CodeAnalysis;

namespace OpenFortiClient.Core.Profiles;

/// <summary>
/// Parses gateway input the way FortiClient accepts it:
/// <c>vpn.example.com</c>, <c>vpn.example.com:10443</c> or <c>https://vpn.example.com:443</c>.
/// </summary>
public static class GatewayAddress
{
    public const int DefaultPort = 443;

    public static bool TryParse(string? input, [NotNullWhen(true)] out Uri? gateway)
    {
        gateway = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        // Normalise to scheme://host:port with no path, so profiles compare and display consistently.
        gateway = new UriBuilder(Uri.UriSchemeHttps, uri.Host, uri.IsDefaultPort ? DefaultPort : uri.Port).Uri;
        return true;
    }
}
