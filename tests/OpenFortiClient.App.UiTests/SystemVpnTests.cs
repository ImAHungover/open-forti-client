using System.Text.Json.Nodes;

namespace OpenFortiClient.App.UiTests;

/// <summary>The macOS VPN card and menu bar lines: the company L2TP VPN set up in System Settings (FakeSystemVpns).</summary>
[Collection(UiCollection.Name)]
public sealed class SystemVpnTests
{
    private const string Id = "7A1C2E44-5B6D-4F80-9A3B-2C4D5E6F7081";

    [Fact]
    public void Without_macOS_VPN_services_there_is_no_card()
    {
        using var app = AppSession.Start(new Scenario());

        app.WaitForText("StatusLabel", "Not connected");

        Assert.False(app.IsVisible("SystemVpnCard"));
    }

    [Fact]
    [Trait("Suite", "Quick")]
    public void Shows_the_L2TP_VPN_and_switches_it_on_and_off()
    {
        using var app = Start(connected: false);
        app.WaitForText("SystemVpnName", "VPN");
        app.WaitForText("SystemVpnStatus", "Not connected");

        app.Tap("SystemVpnSwitch");
        app.WaitForText("SystemVpnStatus", "Connected since");
        app.WaitForText("SystemVpnStatus", "192.168.2.2");

        app.Tap("SystemVpnSwitch");
        app.WaitForText("SystemVpnStatus", "Not connected");
        Assert.Equal([$"start {Id}", $"stop {Id}"], AppSession.WaitForLines(() => app.SystemVpnCalls, 2));
    }

    [Fact]
    public void Keep_it_up_reconnects_after_a_drop()
    {
        using var app = Start(connected: true);
        app.WaitForText("SystemVpnStatus", "Connected since");
        app.Tap("SystemVpnKeepUpSwitch");

        app.Trigger("system-vpn-drop");

        app.WaitForText("SystemVpnError", "VPN lost its connection.");
        app.WaitForText("SystemVpnStatus", "Connected since", TimeSpan.FromSeconds(15));
        Assert.Contains($"start {Id}", app.SystemVpnCalls);
        Assert.Contains(AppSession.WaitForLines(() => app.SystemNotifications, 1), n => n.Contains("macOS VPN connection lost", StringComparison.Ordinal));
    }

    [Fact]
    public void Keep_it_up_brings_the_VPN_up_when_the_app_starts_and_respects_a_switch_off_in_System_Settings()
    {
        using var app = Start(connected: false, keepUp: true);
        app.WaitForText("SystemVpnStatus", "Connected since");

        app.Trigger("system-vpn-off-elsewhere");

        app.WaitForText("SystemVpnStatus", "Not connected");
        Thread.Sleep(TimeSpan.FromSeconds(3));
        Assert.Equal([$"start {Id}"], app.SystemVpnCalls);
        Assert.False(app.IsVisible("SystemVpnError"));
    }

    [Fact]
    public void macOS_refusing_to_start_it_says_what_to_do()
    {
        using var app = Start(connected: false, keepUp: true, beforeLaunch: d => File.WriteAllText(Path.Combine(d, "system-vpn-start-fails"), ""));

        app.WaitForText("SystemVpnError", "macOS could not start VPN: No service. Connect it once in System Settings and save its password.");
        Thread.Sleep(TimeSpan.FromSeconds(3));
        Assert.Equal([$"start {Id}"], app.SystemVpnCalls);
        Assert.Contains(AppSession.WaitForLines(() => app.SystemNotifications, 1), n => n.Contains("macOS VPN connection failed", StringComparison.Ordinal));
    }

    [Fact]
    public void The_menu_bar_line_switches_it()
    {
        using var app = Start(connected: false);
        app.WaitForText("SystemVpnStatus", "Not connected");

        app.MenuBarItem("VPN (L2TP): Not connected").Click();

        app.WaitForText("SystemVpnStatus", "Connected since");
        Assert.Equal([$"start {Id}"], AppSession.WaitForLines(() => app.SystemVpnCalls, 1));
    }

    private static AppSession Start(bool connected, bool keepUp = false, Action<string>? beforeLaunch = null) =>
        AppSession.Start(
            new Scenario(),
            settings: s =>
            {
                s["reconnectDelaysSeconds"] = new JsonArray(1);
                if (keepUp)
                {
                    s["keepUpSystemVpns"] = new JsonArray(Id);
                }
            },
            beforeLaunch: directory =>
            {
                File.WriteAllText(Path.Combine(directory, "system-vpns.json"), $$"""
                    { "services": [ { "id": "{{Id}}", "name": "VPN", "kind": "L2TP", "status": "{{(connected ? "connected" : "disconnected")}}" } ] }
                    """);
                beforeLaunch?.Invoke(directory);
            });
}
