using OpenQA.Selenium.Appium;

namespace OpenFortiClient.App.UiTests;

/// <summary>The menu bar icon (src/OpenFortiClient.Native) and living on without a window.</summary>
[Collection(UiCollection.Name)]
public sealed class MenuBarTests
{
    [Fact]
    public void Connects_and_disconnects_from_the_menu_bar()
    {
        using var app = AppSession.Start(new Scenario().Process(p => p.Emit(Scenario.TunnelUp)), savedPassword: "from-keychain");
        app.WaitForText("StatusLabel", "Not connected");

        app.MenuBarItem("Connect to Test VPN").Click();
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");

        app.MenuBarItem("Disconnect").Click();
        app.WaitForText("StatusLabel", "Not connected");
    }

    [Fact]
    [Trait("Suite", "Quick")]
    public void Closing_the_window_keeps_the_tunnel_and_the_menu_brings_the_window_back()
    {
        using var app = AppSession.Start(new Scenario().Process(p => p.Emit(Scenario.TunnelUp)));
        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");

        app.Driver.FindElement(MobileBy.AccessibilityId("_XCUI:CloseWindow")).Click();
        Assert.Null(AppSession.WaitFor(() => app.IsVisible("StatusLabel") ? "still open" : null, TimeSpan.FromSeconds(2)));
        Assert.DoesNotContain("Logged out", app.AppLog);

        app.MenuBarItem("Open OpenFortiClient").Click();

        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
        Assert.Single(app.StartedGateways);
    }

    [Fact]
    public void Quit_from_the_menu_disconnects_first()
    {
        using var app = AppSession.Start(new Scenario().Process(p => p.Emit(Scenario.TunnelUp)));
        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");

        app.MenuBarItem("Quit OpenFortiClient").Click();

        // The scripted openfortivpn prints "Logged out." when it is terminated, as the real one does.
        Assert.NotNull(AppSession.WaitFor(() => app.AppLog.Contains("Logged out.") ? "ok" : null));
        Assert.Contains("Disconnecting", app.AppLog);
    }
}
