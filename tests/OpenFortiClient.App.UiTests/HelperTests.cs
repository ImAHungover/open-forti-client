using System.Text.Json.Nodes;

namespace OpenFortiClient.App.UiTests;

/// <summary>The privileged helper's Settings section and its use when connecting (fake helper control).</summary>
[Collection(UiCollection.Name)]
public sealed class HelperTests
{
    [Fact]
    public void Installs_the_helper_and_waits_for_approval_in_system_settings()
    {
        using var app = AppSession.Start(new Scenario());
        app.Titled("Settings").Click();
        app.WaitForText("HelperStatusLabel", "Not installed.");

        app.Tap("HelperInstallButton");

        app.WaitForText("HelperStatusLabel", "waiting for your approval");
        Assert.True(File.Exists(Path.Combine(app.Directory, "login-items-opened")), "Install opens Login Items for the approval.");

        app.Trigger("helper-approve");
        app.Tap("HelperRefreshButton");
        app.WaitForText("HelperStatusLabel", "Installed and allowed.");

        app.Tap("HelperRemoveButton");
        app.WaitForText("HelperStatusLabel", "Not installed.");
    }

    [Fact]
    public void Connecting_without_the_helper_says_how_to_install_it()
    {
        using var app = AppSession.Start(new Scenario().Process(p => p.Emit(Scenario.TunnelUp)), settings: s => s["usePrivilegedHelper"] = true);

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");

        app.WaitForText("ErrorLabel", "Install it under Settings → Privileged helper.");
        Assert.Empty(app.StartedGateways);
    }

    [Fact]
    [Trait("Suite", "Quick")]
    public void Connects_through_the_approved_helper()
    {
        using var app = AppSession.Start(new Scenario().Process(p => p.Emit(Scenario.TunnelUp)), settings: s => s["usePrivilegedHelper"] = true);
        app.Titled("Settings").Click();
        app.Tap("HelperInstallButton");
        app.Trigger("helper-approve");
        app.Tap("HelperRefreshButton");
        app.WaitForText("HelperStatusLabel", "Installed and allowed.");

        app.Titled("Connect").Click();
        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");

        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
    }

    [Fact]
    public void Saving_the_settings_keeps_the_ones_not_on_the_page()
    {
        // The Settings page used to save a fresh AppSettings and drop the hidden reconnect delays.
        using var app = AppSession.Start(new Scenario(), settings: s => s["reconnectDelaysSeconds"] = new JsonArray(7));
        app.Titled("Settings").Click();
        app.WaitForText("HelperStatusLabel", "Not installed.");

        app.Tap("SaveSettingsButton");
        app.DialogButton("OK").Click();

        var json = File.ReadAllText(Path.Combine(app.Directory, "data", "settings.json"));
        Assert.Contains("\"reconnectDelaysSeconds\"", json);
        Assert.Contains("7", json);
    }
}
