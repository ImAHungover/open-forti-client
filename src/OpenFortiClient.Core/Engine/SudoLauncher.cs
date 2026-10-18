using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

namespace OpenFortiClient.Core.Engine;

/// <summary>
/// Runs openfortivpn through <c>sudo -n</c>, for development builds; release builds use the privileged
/// helper. Needs a NOPASSWD sudoers rule (scripts/install-sudoers-rule.sh); without one, sudo fails at once
/// with "a password is required" instead of hanging on a prompt. The config goes to a 0600 file in
/// <paramref name="runtimeDirectory"/>, deleted at the first output line.
/// </summary>
public sealed class SudoLauncher(string runtimeDirectory) : IPrivilegedLauncher
{
    public const string SudoPath = "/usr/bin/sudo";

    public IPrivilegedProcess Start(LaunchRequest request)
    {
        var files = LaunchFiles.Write(request, runtimeDirectory);
        try
        {
            return Start(request.Executable, ["-c", files.ConfigPath], request.PromptMarkers, files);
        }
        catch
        {
            files.Dispose();
            throw;
        }
    }

    private static SudoProcess Start(string executable, IReadOnlyList<string> arguments, IReadOnlyList<string> promptMarkers, LaunchFiles files)
    {
        var startInfo = new ProcessStartInfo(SudoPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(executable);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {SudoPath}.");
        return new SudoProcess(process, promptMarkers, files);
    }

    private sealed class SudoProcess : IPrivilegedProcess
    {
        private const int SigTerm = 15;

        private readonly Process _process;
        private readonly IReadOnlyList<string> _promptMarkers;
        private readonly Channel<string> _output = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly LaunchFiles _files;

        public SudoProcess(Process process, IReadOnlyList<string> promptMarkers, LaunchFiles files)
        {
            _process = process;
            _promptMarkers = promptMarkers;
            _files = files;
            _ = RunAsync();
        }

        public ChannelReader<string> Output => _output.Reader;

        public Task<int> Exited => _exited.Task;

        public async Task WriteLineAsync(string text, CancellationToken ct = default)
        {
            await _process.StandardInput.WriteLineAsync(text.AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);
        }

        public void Terminate()
        {
            if (!_process.HasExited)
            {
                // sudo forwards SIGTERM to openfortivpn. Process.Kill would send SIGKILL and
                // leave routes and DNS behind.
                _ = Kill(_process.Id, SigTerm);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Terminate();
            await Task.WhenAny(Exited, Task.Delay(TimeSpan.FromSeconds(5)));
            _process.Dispose();
        }

        private async Task RunAsync()
        {
            try
            {
                await Task.WhenAll(PumpAsync(_process.StandardOutput), PumpAsync(_process.StandardError));
                await _process.WaitForExitAsync();
                _exited.TrySetResult(_process.ExitCode);
            }
            catch (Exception ex)
            {
                _exited.TrySetException(ex);
            }
            finally
            {
                _files.Dispose();
                _output.Writer.TryComplete();
            }
        }

        private async Task PumpAsync(StreamReader reader)
        {
            var buffer = new char[1024];
            var pending = new StringBuilder();
            int read;
            while ((read = await reader.ReadAsync(buffer)) > 0)
            {
                foreach (var c in buffer.AsSpan(0, read))
                {
                    if (c == '\n')
                    {
                        Emit(pending);
                    }
                    else if (c != '\r')
                    {
                        pending.Append(c);
                    }
                }

                if (EndsWithPrompt(pending))
                {
                    Emit(pending);
                }
            }

            if (pending.Length > 0)
            {
                Emit(pending);
            }
        }

        private bool EndsWithPrompt(StringBuilder pending)
        {
            var text = pending.ToString().TrimEnd();
            return text.Length > 0 && _promptMarkers.Any(m => text.EndsWith(m, StringComparison.Ordinal));
        }

        private void Emit(StringBuilder pending)
        {
            // openfortivpn has read its config before it prints anything; the key stays until it exits.
            _files.DeleteConfig();
            _output.Writer.TryWrite(pending.ToString());
            pending.Clear();
        }

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        private static extern int Kill(int pid, int signal);
    }
}
