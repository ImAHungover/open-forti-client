namespace OpenFortiClient.App.UiTests;

[Collection(UiCollection.Name)]
public sealed class AutoConnectTests
{
    [Fact]
    public void Connects_on_start_when_the_login_is_saved()
    {
        using var app = AppSession.Start(
            new Scenario().Process(p => p.Emit(Scenario.TunnelUp)),
            savedPassword: "from-keychain",
            profile: p => p["autoConnect"] = true);

        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
    }

    [Fact]
    public void Skips_auto_connect_without_a_saved_password()
    {
        using var app = AppSession.Start(new Scenario().Process(p => p.Emit(Scenario.TunnelUp)), profile: p => p["autoConnect"] = true);
        app.WaitForText("StatusLabel", "Not connected");

        app.Titled("Notifications").Click();

        var messages = AppSession.WaitFor(() => app.Texts("NotificationMessage") is { Count: >= 1 } t ? t : null) ?? [];
        Assert.Equal(["Auto-connect skipped: Test VPN needs a password."], messages);
        Assert.Empty(app.StartedGateways);
    }

    [Fact]
    public void The_editor_saves_auto_connect_and_always_up()
    {
        using var app = AppSession.Start(new Scenario());
        app.WaitForText("ProfilePicker", "Test VPN");

        app.Tap("ManageConnectionsButton");
        app.DialogButton("Edit the selected connection").Click();
        app.Tap("AutoConnectCheckBox");
        app.Tap("AlwaysUpCheckBox");
        app.Tap("SaveButton");
        app.Element("ConnectButton");

        var json = AppSession.WaitFor(() => app.ProfilesJson.Contains("\"alwaysUp\": true") ? app.ProfilesJson : null) ?? app.ProfilesJson;
        Assert.Contains("\"autoConnect\": true", json);
        Assert.Contains("\"alwaysUp\": true", json);
    }

    [Fact]
    public void Auto_connects_even_when_a_leftover_config_file_cannot_be_removed()
    {
        // Review issue 6: one failure during start-up disabled auto-connect for the whole session.
        using var app = AppSession.Start(
            new Scenario().Process(p => p.Emit(Scenario.TunnelUp)),
            savedPassword: "from-keychain",
            profile: p => p["autoConnect"] = true,
            beforeLaunch: directory =>
            {
                // An immutable leftover: File.Delete on it fails, but the folder stays writable for the engine.
                var run = Path.Combine(directory, "data", "run");
                Directory.CreateDirectory(run);
                var leftover = Path.Combine(run, "leftover.conf");
                File.WriteAllText(leftover, "");
                System.Diagnostics.Process.Start("/usr/bin/chflags", ["uchg", leftover])!.WaitForExit();
            });

        try
        {
            app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
        }
        finally
        {
            System.Diagnostics.Process.Start("/usr/bin/chflags", ["nouchg", Path.Combine(app.Directory, "data", "run", "leftover.conf")])!.WaitForExit();
        }
    }
}
