using System.Threading.Channels;
using Foundation;
using ObjCRuntime;
using OpenFortiClient.Core.Engine;

namespace OpenFortiClient.App;

/// <summary>
/// Starts openfortivpn through the privileged helper (src/OpenFortiClient.Helper) over XPC, using
/// OFCHelperClient from the native library: one connection per start. The config and key go to the helper
/// as text; it writes them where only root can read them.
/// </summary>
public sealed class HelperLauncher : IPrivilegedLauncher
{
    public IPrivilegedProcess Start(LaunchRequest request)
    {
        var process = new HelperProcess();
        process.Start(request);
        return process;
    }

    private sealed class HelperProcess : NSObject, IPrivilegedProcess
    {
        private readonly Channel<string> _output = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private NSObject? _client;

        public ChannelReader<string> Output => _output.Reader;

        public Task<int> Exited => _exited.Task;

        public void Start(LaunchRequest request)
        {
            _client = NativeBridge.Create("OFCHelperClient", out var error)
                ?? throw new InvalidOperationException(error ?? "the helper client did not load.");
            NativeBridge.Call(_client, "setDelegate:", this);

            var arguments = new NSMutableDictionary
            {
                ["executable"] = new NSString(request.Executable),
                ["config"] = new NSString(request.ConfigText),
                ["promptMarkers"] = NSArray.FromStrings([.. request.PromptMarkers]),
            };
            if (request.ClientKeyPem is { } key)
            {
                arguments["clientKey"] = new NSString(key);
            }

            // Blocks until the helper answered; the engine calls this off the main thread.
            if (NativeBridge.CallForString(_client, "start:", arguments) is { } failure)
            {
                throw new InvalidOperationException(failure);
            }
        }

        public Task WriteLineAsync(string text, CancellationToken ct = default)
        {
            if (_exited.Task.IsCompleted)
            {
                throw new IOException("openfortivpn has exited.");
            }

            if (_client is not null)
            {
                NativeBridge.Call(_client, "write:", new NSString(text));
            }
            return Task.CompletedTask;
        }

        public void Terminate()
        {
            if (_client is not null)
            {
                NativeBridge.Call(_client, "terminate");
            }
        }

        public async ValueTask DisposeAsync()
        {
            Terminate();
            await Task.WhenAny(Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        }

        [Export("helperOutput:")]
        public void HelperOutput(NSString line) => _output.Writer.TryWrite(line.ToString());

        [Export("helperExited:")]
        public void HelperExited(NSNumber code)
        {
            _output.Writer.TryComplete();
            _exited.TrySetResult(code.Int32Value);
        }
    }
}
