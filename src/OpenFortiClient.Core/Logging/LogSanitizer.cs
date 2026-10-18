using System.Text.RegularExpressions;

namespace OpenFortiClient.Core.Logging;

/// <summary>
/// Masks passwords, one-time passwords and session cookies before a line is logged.
/// </summary>
public static partial class LogSanitizer
{
    public const string Mask = "***";

    public static string Sanitize(string line)
    {
        line = SecretAssignment().Replace(line, m => m.Groups["key"].Value + Mask);
        line = SessionCookie().Replace(line, m => m.Groups["key"].Value + Mask);
        return line;
    }

    // password = "x", Configuration password = "x", One-time password = "x", otp: x, -p x, --password=x
    [GeneratedRegex(
        """(?<key>\b(?:password|otp)\s*[=:]\s*|(?:^|\s)(?:-p|--password|-o|--otp)(?:=|\s+))(?:"[^"]*"|\S+)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretAssignment();

    // SVPNCOOKIE=abc; Cookie: abc
    [GeneratedRegex("""(?<key>(?:SVPNCOOKIE\s*=|\bcookie\s*[=:])\s*)(?!SVPNCOOKIE)[^;\s]+""", RegexOptions.IgnoreCase)]
    private static partial Regex SessionCookie();
}
