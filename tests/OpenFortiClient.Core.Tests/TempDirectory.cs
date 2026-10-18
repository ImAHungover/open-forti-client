namespace OpenFortiClient.Core.Tests;

/// <summary>A temp folder that is deleted after the test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ofc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public AppPaths AppPaths => new(System.IO.Path.Combine(Path, "data"), System.IO.Path.Combine(Path, "logs"));

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
