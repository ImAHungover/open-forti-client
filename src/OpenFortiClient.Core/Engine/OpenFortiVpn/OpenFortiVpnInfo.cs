using System.Diagnostics;

namespace OpenFortiClient.Core.Engine.OpenFortiVpn;

public static class OpenFortiVpnInfo
{
    /// <summary>Runs <c>openfortivpn --version</c> (no root needed). Returns null if it is missing or fails.</summary>
    public static async Task<string?> GetVersionAsync(string executablePath, CancellationToken ct = default)
    {
        if (!File.Exists(executablePath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(executablePath, "--version")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
