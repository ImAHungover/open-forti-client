using System.Diagnostics;

// The UI tests drive the Mac app; they only run on macOS.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("macos")]

namespace OpenFortiClient.App.UiTests;

/// <summary>Starts an Appium server for the test run, unless one is already listening.</summary>
public sealed class AppiumServer : IAsyncLifetime
{
    public static readonly Uri Url = new("http://127.0.0.1:4723/");

    private Process? _process;

    public async Task InitializeAsync()
    {
        if (await IsUpAsync())
        {
            return;
        }

        var appium = new[] { "/opt/homebrew/bin/appium", "/usr/local/bin/appium" }.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("Appium not found. Install it as described in docs/development.md, \"End-to-end UI tests\".");

        _process = Process.Start(new ProcessStartInfo(appium, ["--port", "4723", "--log-level", "warn"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });

        for (var i = 0; i < 60 && !await IsUpAsync(); i++)
        {
            await Task.Delay(500);
        }

        if (!await IsUpAsync())
        {
            throw new InvalidOperationException("Appium did not start within 30 seconds.");
        }
    }

    public Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
        }

        _process?.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<bool> IsUpAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            return (await http.GetAsync(new Uri(Url, "status"))).IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}

/// <summary>One Appium server for all UI tests; they run one after the other, since they share the screen.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiCollection : ICollectionFixture<AppiumServer>
{
    public const string Name = "UI";
}
