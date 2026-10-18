using System.Text.Json.Nodes;

namespace OpenFortiClient.App.UiTests;

/// <summary>
/// What the scripted openfortivpn does on each start; written to scenario.json for the app's
/// ScriptedLauncher (src/OpenFortiClient.App/Services/EndToEnd).
/// </summary>
public sealed class Scenario
{
    private readonly JsonArray _processes = [];

    /// <summary>The lines openfortivpn 1.24.1 prints for a working connection.</summary>
    public static readonly string[] TunnelUp =
    [
        "INFO:   Connected to gateway.",
        "INFO:   Authenticated.",
        "INFO:   Remote gateway has allocated a VPN.",
        "INFO:   Tunnel is up and running.",
    ];

    public Scenario Process(Action<Steps> steps)
    {
        var builder = new Steps();
        steps(builder);
        _processes.Add(new JsonObject { ["steps"] = builder.Array });
        return this;
    }

    public string ToJson() => new JsonObject { ["processes"] = _processes }.ToJsonString();

    public sealed class Steps
    {

        internal JsonArray Array { get; } = [];

        public Steps Emit(params string[] lines)
        {
            foreach (var line in lines)
            {
                Array.Add(new JsonObject { ["emit"] = line });
            }

            return this;
        }

        public Steps Delay(int milliseconds) => Add("delayMs", milliseconds);

        /// <summary>Waits for the app to write a line (the OTP) to stdin.</summary>
        public Steps AwaitInput() => Add("awaitInput", true);

        /// <summary>Waits until the test calls <see cref="AppSession.Trigger"/> with this name.</summary>
        public Steps WaitFor(string trigger) => Add("waitForFile", trigger);

        public Steps Exit(int code) => Add("exit", code);

        private Steps Add(string kind, JsonNode value)
        {
            Array.Add(new JsonObject { [kind] = value });
            return this;
        }
    }
}
