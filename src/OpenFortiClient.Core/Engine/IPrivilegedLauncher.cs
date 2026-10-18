using System.Threading.Channels;

namespace OpenFortiClient.Core.Engine;

/// <summary>
/// One openfortivpn to start as root. The config is text, not a file: where it is written (if at all)
/// is the launcher's business, so the privileged helper can keep it on a disk only root can read.
/// </summary>
/// <param name="ConfigText">The openfortivpn config, password included. Never logged.</param>
/// <param name="PromptMarkers">
/// Text that marks an input prompt. Prompts have no trailing newline, so output that ends with one of
/// these is delivered as a line straight away instead of waiting for a newline.
/// </param>
public sealed record LaunchRequest(string Executable, string ConfigText, IReadOnlyList<string> PromptMarkers)
{
    /// <summary>The client private key (PEM) from the Keychain; the launcher writes it next to the config.</summary>
    public string? ClientKeyPem { get; init; }

    // Records print every property by default; never print the config or the key.
    public override string ToString() => $"LaunchRequest {{ Executable = {Executable} }}";
}

/// <summary>
/// Starts openfortivpn as root: the privileged helper (release builds) or <see cref="SudoLauncher"/>
/// (development). See docs/architecture.md, "Elevation".
/// </summary>
public interface IPrivilegedLauncher
{
    IPrivilegedProcess Start(LaunchRequest request);
}

public interface IPrivilegedProcess : IAsyncDisposable
{
    /// <summary>stdout and stderr, line by line. Completes when the process exits.</summary>
    ChannelReader<string> Output { get; }

    /// <summary>Completes with the exit code.</summary>
    Task<int> Exited { get; }

    Task WriteLineAsync(string text, CancellationToken ct = default);

    /// <summary>Asks the process to stop (SIGTERM), so openfortivpn can remove its routes and DNS.</summary>
    void Terminate();
}
