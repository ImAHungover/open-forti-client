using System.Text.Json.Nodes;

namespace OpenFortiClient.App.UiTests;

/// <summary>"Always up": reconnecting by itself after a drop, as needed with a flaky company L2TP VPN.</summary>
[Collection(UiCollection.Name)]
public sealed class AlwaysUpTests
{
    private static readonly Action<Scenario.Steps> DropAfterUp = p => p.Emit(Scenario.TunnelUp).WaitFor("drop").Emit("INFO:   Cancelling threads...");

    [Fact]
    [Trait("Suite", "Quick")]
    public void Reconnects_by_itself_after_a_drop()
    {
        using var app = Start(new Scenario().Process(DropAfterUp).Process(p => p.Emit(Scenario.TunnelUp)), delays: [1]);
        Connect(app);

        app.Trigger("drop");

        AppSession.WaitForLines(() => app.StartedGateways, 2);
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
        Assert.Equal(
            [$"Info|VPN connected|Connected to {AppSession.Gateway1}.",
             $"Error|VPN connection lost|The connection to {AppSession.Gateway1} was lost.",
             $"Info|VPN reconnected|Reconnected to {AppSession.Gateway1}."],
            AppSession.WaitForLines(() => app.SystemNotifications, 3));
    }

    [Fact]
    public void Stays_disconnected_without_always_up()
    {
        using var app = Start(new Scenario().Process(DropAfterUp).Process(p => p.Emit(Scenario.TunnelUp)), delays: [1], alwaysUp: false);
        Connect(app);

        app.Trigger("drop");
        app.WaitForText("ErrorLabel", "was lost");
        Thread.Sleep(1500); // a 1 s reconnect delay would have started by now

        Assert.Single(app.StartedGateways);
        Assert.True(app.IsVisible("ConnectButton"));
    }

    [Fact]
    public void Waits_after_a_failed_reconnect_and_retries_at_once_when_the_network_changes()
    {
        using var app = Start(
            new Scenario()
                .Process(DropAfterUp)
                .Process(p => p.Emit("ERROR:  connect: Connection refused").Exit(1)) // reconnect, vpn1
                .Process(p => p.Emit("ERROR:  connect: Connection refused").Exit(1)) // reconnect, vpn2
                .Process(p => p.Emit(Scenario.TunnelUp)),
            delays: [1, 120]);
        Connect(app);

        app.Trigger("drop");
        app.WaitForText("StatusLabel", "(attempt 2)");
        Assert.Contains("Reconnecting in 1", app.WaitForText("StatusLabel", "Reconnecting in"));

        app.Trigger("network-change");

        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}", TimeSpan.FromSeconds(10));
        Assert.Equal(4, app.StartedGateways.Count);
    }

    [Fact]
    public void Stop_reconnecting_ends_the_wait()
    {
        using var app = Start(new Scenario().Process(DropAfterUp).Process(p => p.Emit(Scenario.TunnelUp)), delays: [120]);
        Connect(app);

        app.Trigger("drop");
        app.WaitForText("StatusLabel", "Reconnecting in");
        app.WaitForText("DisconnectButton", "Stop reconnecting");
        app.Tap("DisconnectButton");

        app.WaitForText("StatusLabel", "Not connected");
        Assert.True(app.IsVisible("ConnectButton"));
        app.Titled("Notifications").Click();
        var messages = AppSession.WaitFor(() => app.Texts("NotificationMessage") is { Count: >= 1 } t && t[0] == "Reconnecting stopped." ? t : null);
        Assert.NotNull(messages);
    }

    [Fact]
    public void Pauses_with_a_banner_when_the_password_is_rejected()
    {
        using var app = Start(
            new Scenario()
                .Process(DropAfterUp)
                .Process(p => p
                    .Emit("INFO:   Connected to gateway.", "ERROR:  Could not authenticate to gateway. Please check the password, client certificate, etc.")
                    .Exit(1)),
            delays: [1]);
        Connect(app);

        app.Trigger("drop");

        var banners = AppSession.WaitForLines(() => app.SystemNotifications, 3);
        Assert.Equal("Warning|Always up paused|Wrong username or password, or the gateway rejected the login.", banners[^1]);
        app.WaitForText("StatusLabel", "Not connected");
        Assert.Equal(2, app.StartedGateways.Count);
    }

    [Fact]
    public void Connecting_by_hand_replaces_a_waiting_reconnect()
    {
        // Review issue 3: Connect was hidden while a reconnect waited.
        using var app = Start(new Scenario().Process(DropAfterUp).Process(p => p.Emit(Scenario.TunnelUp)), delays: [120]);
        Connect(app);
        app.Trigger("drop");
        app.WaitForText("StatusLabel", "Reconnecting in");

        app.Tap("ConnectButton");

        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
        Assert.Equal(2, app.StartedGateways.Count);
    }

    [Fact]
    public void Turning_always_up_off_stops_a_waiting_reconnect()
    {
        // Review issue 4: the loop kept going after the option was turned off.
        using var app = Start(new Scenario().Process(DropAfterUp).Process(p => p.Emit(Scenario.TunnelUp)), delays: [120]);
        Connect(app);
        app.Trigger("drop");
        app.WaitForText("StatusLabel", "Reconnecting in");

        app.Tap("ManageConnectionsButton");
        app.DialogButton("Edit the selected connection").Click();
        app.Tap("AlwaysUpCheckBox");
        app.Tap("SaveButton");
        app.Trigger("network-change");

        app.WaitForText("StatusLabel", "Not connected");
        Thread.Sleep(1000); // the network trigger is polled every 100 ms
        Assert.Single(app.StartedGateways);
    }

    [Fact]
    public void Deleting_the_connection_stops_reconnecting_and_records_it()
    {
        // Review issue 5: a reconnect that found no profile stopped silently.
        using var app = Start(new Scenario().Process(DropAfterUp).Process(p => p.Emit(Scenario.TunnelUp)), delays: [120]);
        Connect(app);
        app.Trigger("drop");
        app.WaitForText("StatusLabel", "Reconnecting in");

        app.Tap("ManageConnectionsButton");
        app.DialogButton("Delete the selected connection").Click();
        app.DialogButton("Delete").Click();
        Assert.NotNull(AppSession.WaitFor(() => app.ProfilesJson.Contains("Test VPN") ? null : "deleted"));
        app.Trigger("network-change");
        Thread.Sleep(1000); // the network trigger is polled every 100 ms

        Assert.Single(app.StartedGateways);
        app.Titled("Notifications").Click();
        Assert.NotNull(AppSession.WaitFor(() => app.Texts("NotificationMessage") is { Count: >= 1 } t && t[0] == "Reconnecting stopped." ? t : null));
    }

    private static AppSession Start(Scenario scenario, int[] delays, bool alwaysUp = true) =>
        AppSession.Start(
            scenario,
            settings: s => s["reconnectDelaysSeconds"] = new JsonArray([.. delays.Select(d => (JsonNode)d)]),
            profile: p => p["alwaysUp"] = alwaysUp);

    private static void Connect(AppSession app)
    {
        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
    }
}
