#if E2E
using System.Text.Json;
using System.Threading.Channels;
using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.App.Services.EndToEnd;

/// <summary>
/// Stands in for openfortivpn in the end-to-end tests. Each start replays the next entry of
/// <c>scenario.json</c> in the test folder:
/// <code>{"processes":[{"steps":[{"emit":"INFO:   Tunnel is up and running."},{"waitForFile":"drop"},{"emit":"INFO:   Cancelling threads..."}]}]}</code>
/// Steps: <c>emit</c> a line, <c>delayMs</c>, <c>awaitInput</c> (an OTP, appended to input.log),
/// <c>waitForFile</c> (until the test creates that file), <c>exit</c> with a code. A process whose
/// steps run out keeps running until it is terminated. Each start appends the gateway host to started.log, and the certificates it trusts to trusted.log.
/// </summary>
public sealed class ScriptedLauncher(string directory) : IPrivilegedLauncher
{
    private int _starts;

    public IPrivilegedProcess Start(LaunchRequest request)
    {
        var index = Interlocked.Increment(ref _starts) - 1;

        // Record only the host and trusted certificates: the config also holds the password.
        var config = request.ConfigText.Split('\n');
        var host = config.FirstOrDefault(l => l.StartsWith("host = ", StringComparison.Ordinal))?[7..] ?? "?";
        File.AppendAllText(Path.Combine(directory, "started.log"), host + "\n");
        if (request.ClientKeyPem is { } pem)
        {
            File.AppendAllText(Path.Combine(directory, "client-key.log"), $"{index + 1} {pem.Split('\n')[0]}\n");
        }

        foreach (var digest in config.Where(l => l.StartsWith("trusted-cert = ", StringComparison.Ordinal)))
        {
            File.AppendAllText(Path.Combine(directory, "trusted.log"), $"{index + 1} {digest[15..]}\n");
        }

        return new ScriptedProcess(LoadSteps(index), directory);
    }

    private List<Step> LoadSteps(int index)
    {
        using var scenario = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "scenario.json")));
        var processes = scenario.RootElement.GetProperty("processes");
        if (index >= processes.GetArrayLength())
        {
            return [new Step("emit", $"ERROR:  E2E scenario has no script for start {index + 1}.", 0), new Step("exit", null, 1)];
        }

        var steps = new List<Step>();
        foreach (var step in processes[index].GetProperty("steps").EnumerateArray())
        {
            var property = step.EnumerateObject().First();
            steps.Add(property.Value.ValueKind == JsonValueKind.Number
                ? new Step(property.Name, null, property.Value.GetInt32())
                : new Step(property.Name, property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null, 0));
        }

        return steps;
    }

    private sealed record Step(string Kind, string? Text, int Number);

    private sealed class ScriptedProcess : IPrivilegedProcess
    {
        private readonly Channel<string> _output = Channel.CreateUnbounded<string>();
        private readonly Channel<string> _input = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _terminate = new();
        private readonly string _directory;

        public ScriptedProcess(IReadOnlyList<Step> steps, string directory)
        {
            _directory = directory;
            _ = RunAsync(steps);
        }

        public ChannelReader<string> Output => _output.Reader;

        public Task<int> Exited => _exited.Task;

        public Task WriteLineAsync(string text, CancellationToken ct = default) => _input.Writer.WriteAsync(text, ct).AsTask();

        public void Terminate() => _terminate.Cancel();

        public async ValueTask DisposeAsync()
        {
            Terminate();
            await Task.WhenAny(Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        }

        private async Task RunAsync(IReadOnlyList<Step> steps)
        {
            var ct = _terminate.Token;
            try
            {
                foreach (var step in steps)
                {
                    ct.ThrowIfCancellationRequested();
                    switch (step.Kind)
                    {
                        case "emit":
                            _output.Writer.TryWrite(step.Text ?? "");
                            break;

                        case "delayMs":
                            await Task.Delay(step.Number, ct);
                            break;

                        case "awaitInput":
                            var line = await _input.Reader.ReadAsync(ct);
                            await File.AppendAllTextAsync(Path.Combine(_directory, "input.log"), line + "\n", ct);
                            break;

                        case "waitForFile":
                            var trigger = Path.Combine(_directory, step.Text ?? "");
                            while (!File.Exists(trigger))
                            {
                                await Task.Delay(100, ct);
                            }

                            File.Delete(trigger);
                            break;

                        case "exit":
                            Finish(step.Number);
                            return;
                    }
                }

                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                // Terminated, as openfortivpn after SIGTERM.
                _output.Writer.TryWrite("INFO:   Cancelling threads...");
                _output.Writer.TryWrite("INFO:   Logged out.");
                Finish(0);
            }
        }

        private void Finish(int exitCode)
        {
            _output.Writer.TryComplete();
            _exited.TrySetResult(exitCode);
        }
    }
}
#endif
