#if E2E
using OpenFortiClient.Core;

namespace OpenFortiClient.App.Services.EndToEnd;

/// <summary>
/// Test mode for the end-to-end UI tests (tests/OpenFortiClient.App.UiTests); only in the E2E build
/// (<c>-p:E2E=true</c>, which defines the E2E symbol), never in a normal Debug or Release build.
/// When <see cref="DirectoryVariable"/> names a folder, the app keeps its data and logs there,
/// stores passwords in a plain file instead of the Keychain, and replays scripted openfortivpn
/// output instead of starting openfortivpn. See docs/development.md, "End-to-end UI tests".
/// </summary>
public static class EndToEndMode
{
    public const string DirectoryVariable = "OPENFORTICLIENT_E2E_DIR";

    public static string? Directory { get; } =
        Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } directory ? directory : null;

    public static AppPaths Paths(string directory) =>
        new(Path.Combine(directory, "data"), Path.Combine(directory, "logs"));
}
#endif
