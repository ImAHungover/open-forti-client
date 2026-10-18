using System.Threading.Channels;
using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.Core.Tests.Fakes;

/// <summary>
/// Hands out scripted processes in order. Each script runs when its process starts
/// and decides what the "openfortivpn" output looks like.
/// </summary>
internal sealed class FakeLauncher(params Action<FakeProcess>[] scripts) : IPrivilegedLauncher
{
    private readonly Queue<Action<FakeProcess>> _scripts = new(scripts);

    public List<FakeProcess> Started { get; } = [];

    public IPrivilegedProcess Start(LaunchRequest request)
    {
        var process = new FakeProcess(request);
        Started.Add(process);
        _scripts.Dequeue()(process);
        return process;
    }
}

internal sealed class FakeProcess(LaunchRequest request) : IPrivilegedProcess
{
    private readonly Channel<string> _output = Channel.CreateUnbounded<string>();
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LaunchRequest Request { get; } = request;

    public string ConfigContent => Request.ConfigText;

    public List<string> Input { get; } = [];

    public bool Terminated { get; private set; }

    public int TerminateCount { get; private set; }

    /// <summary>Like an openfortivpn stuck in a system call: SIGTERM is noted but the process keeps running.</summary>
    public bool IgnoresTerminate { get; set; }

    public Action<FakeProcess, string>? OnInput { get; set; }

    public ChannelReader<string> Output => _output.Reader;

    public Task<int> Exited => _exited.Task;

    public FakeProcess Emit(params string[] lines)
    {
        foreach (var line in lines)
        {
            _output.Writer.TryWrite(line);
        }

        return this;
    }

    public void Exit(int code)
    {
        _output.Writer.TryComplete();
        _exited.TrySetResult(code);
    }

    public Task WriteLineAsync(string text, CancellationToken ct = default)
    {
        // Like a real pipe: stdin of an exited process is closed.
        if (_exited.Task.IsCompleted)
        {
            throw new IOException("Broken pipe");
        }

        Input.Add(text);
        OnInput?.Invoke(this, text);
        return Task.CompletedTask;
    }

    public void Terminate()
    {
        Terminated = true;
        TerminateCount++;
        if (!IgnoresTerminate)
        {
            Exit(0);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!IgnoresTerminate)
        {
            Exit(143);
        }

        return ValueTask.CompletedTask;
    }
}
