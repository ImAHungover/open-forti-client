namespace OpenFortiClient.App.UiTests;

/// <summary>macOS banners. The test build records them in system-notifications.log instead of showing them.</summary>
[Collection(UiCollection.Name)]
public sealed class SystemNotificationsTests
{
    private const string Connected1 = $"Info|VPN connected|Connected to {AppSession.Gateway1}.";

    [Fact]
    public void Shows_a_banner_when_connected_but_not_when_I_disconnect()
    {
        using var app = AppSession.Start(new Scenario().Process(p => p.Emit(Scenario.TunnelUp)));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
        app.Tap("DisconnectButton");
        app.WaitForText("StatusLabel", "Not connected");
        Thread.Sleep(500); // banners are written synchronously with the state change

        Assert.Equal([Connected1], app.SystemNotifications);
    }

    [Fact]
    public void Shows_a_banner_when_the_connection_is_lost()
    {
        using var app = AppSession.Start(new Scenario()
            .Process(p => p.Emit(Scenario.TunnelUp).WaitFor("drop").Emit("INFO:   Cancelling threads...")));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
        app.Trigger("drop");

        Assert.Equal(
            [Connected1, $"Error|VPN connection lost|The connection to {AppSession.Gateway1} was lost."],
            AppSession.WaitForLines(() => app.SystemNotifications, 2));
    }

    [Fact]
    public void Shows_a_banner_when_trying_another_gateway_and_when_connecting_fails()
    {
        using var app = AppSession.Start(new Scenario()
            .Process(p => p.Emit("ERROR:  connect: Connection refused").Exit(1))
            .Process(p => p.Emit("ERROR:  connect: Connection refused").Exit(1)));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");

        var banners = AppSession.WaitForLines(() => app.SystemNotifications, 2);
        Assert.Equal(2, banners.Count);
        Assert.StartsWith($"Warning|Trying another gateway|{AppSession.Gateway1} failed (", banners[0]);
        Assert.EndsWith($"Trying {AppSession.Gateway2}.", banners[0]);
        Assert.StartsWith($"Error|VPN connection failed|Could not reach {AppSession.Gateway2}", banners[1]);
    }

    [Fact]
    public void Shows_no_banners_when_turned_off_in_the_settings()
    {
        using var app = AppSession.Start(
            new Scenario().Process(p => p.Emit(Scenario.TunnelUp)),
            settings: s => s["showSystemNotifications"] = false);

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
        Thread.Sleep(500); // banners are written synchronously with the state change

        Assert.Empty(app.SystemNotifications);
    }

    [Fact]
    public void The_setting_can_be_switched_off_on_the_settings_page()
    {
        using var app = AppSession.Start(new Scenario());

        app.Titled("Settings").Click();
        app.Tap("SystemNotificationsSwitch");
        app.Tap("SaveSettingsButton");
        app.DialogButton("OK").Click();

        Assert.Contains("\"showSystemNotifications\": false", File.ReadAllText(Path.Combine(app.Directory, "data", "settings.json")));
    }
}
