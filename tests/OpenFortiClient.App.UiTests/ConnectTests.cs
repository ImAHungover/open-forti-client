namespace OpenFortiClient.App.UiTests;

/// <summary>The Connect view against a scripted openfortivpn, one scenario per test.</summary>
[Collection(UiCollection.Name)]
public sealed class ConnectTests
{
    [Fact]
    [Trait("Suite", "Quick")]
    public void Connects_and_disconnects()
    {
        using var app = AppSession.Start(new Scenario().Process(p => p.Emit(Scenario.TunnelUp)));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");

        app.Tap("DisconnectButton");
        app.WaitForText("StatusLabel", "Not connected");
        Assert.True(app.IsVisible("ConnectButton"));
    }

    [Fact]
    public void Reports_a_lost_connection_straight_away_and_can_reconnect()
    {
        // As seen on 2026-10-17: the link drops, and openfortivpn keeps running to log out.
        using var app = AppSession.Start(new Scenario()
            .Process(p => p.Emit(Scenario.TunnelUp).WaitFor("drop").Emit("INFO:   Cancelling threads...", "INFO:   Setting ppp0 interface down."))
            .Process(p => p.Emit(Scenario.TunnelUp)));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");

        app.Trigger("drop");
        app.WaitForText("ErrorLabel", $"The connection to {AppSession.Gateway1} was lost.", TimeSpan.FromSeconds(5));
        app.WaitForText("StatusLabel", "Not connected");

        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
    }

    [Fact]
    [Trait("Suite", "Quick")]
    public void Fails_over_to_the_second_gateway()
    {
        using var app = AppSession.Start(new Scenario()
            .Process(p => p.Emit("ERROR:  connect: Connection refused").Exit(1))
            .Process(p => p.Emit(Scenario.TunnelUp)));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");

        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway2}");
        Assert.Equal([AppSession.Gateway1, AppSession.Gateway2], app.StartedGateways);
    }

    [Fact]
    public void Stops_when_the_tunnel_cannot_be_set_up_after_login()
    {
        // As seen on 2026-10-17: login works, then the route to the gateway dies.
        using var app = AppSession.Start(new Scenario()
            .Process(p => p.Emit(
                "INFO:   Connected to gateway.",
                "INFO:   Authenticated.",
                "INFO:   Remote gateway has allocated a VPN.",
                "ERROR:  connect: Operation timed out",
                "ERROR:  Could not get VPN configuration (TLS error).",
                "INFO:   Closed connection to gateway."))
            .Process(p => p.Emit("ERROR:  connect: Operation timed out").Exit(1)));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");

        app.WaitForText("ErrorLabel", "Could not reach", TimeSpan.FromSeconds(10));
        Assert.Equal([AppSession.Gateway1, AppSession.Gateway2], app.StartedGateways);
    }

    [Fact]
    public void Reports_a_wrong_password_without_trying_the_other_gateway()
    {
        using var app = AppSession.Start(new Scenario()
            .Process(p => p
                .Emit("INFO:   Connected to gateway.", "ERROR:  Could not authenticate to gateway. Please check the password, client certificate, etc.")
                .Exit(1)));

        app.Type("PasswordEntry", "wrong");
        app.Tap("ConnectButton");

        app.WaitForText("ErrorLabel", "Wrong username or password");
        Assert.Equal([AppSession.Gateway1], app.StartedGateways);
    }

    [Fact]
    [Trait("Suite", "Quick")]
    public void Asks_for_the_one_time_password()
    {
        using var app = AppSession.Start(new Scenario()
            .Process(p => p
                .Emit("INFO:   Connected to gateway.", "Two-factor authentication token: ")
                .AwaitInput()
                .Emit("INFO:   Authenticated.", "INFO:   Tunnel is up and running.")));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");

        app.TypeIntoAlert("123456");
        app.DialogButton("OK").Click();

        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
        Assert.Equal(["123456"], app.Input);
    }

    [Fact]
    [Trait("Suite", "Quick")]
    public void Asks_before_trusting_an_unknown_certificate_and_remembers_it()
    {
        const string digest = "4daa8c5fe6c8b1a1f0e3c2d4b5a69788796a5b4c3d2e1f0a9b8c7d6e5f4a3b2c";
        using var app = AppSession.Start(new Scenario()
            .Process(p => p
                .Emit(
                    "ERROR:  Gateway certificate validation failed, and the certificate digest is not in the local whitelist. If you trust it, rerun with:",
                    $"ERROR:      --trusted-cert {digest}")
                .Exit(1))
            .Process(p => p.Emit(Scenario.TunnelUp)));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");

        app.DialogButton("Trust and connect").Click();

        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
        Assert.Contains(digest, app.ProfilesJson);
    }

    [Fact]
    public void A_trusted_certificate_is_used_when_connecting_again_from_the_same_screen()
    {
        // Review issue 10: the Connect screen kept the profile from before the certificate was trusted.
        const string digest = "4daa8c5fe6c8b1a1f0e3c2d4b5a69788796a5b4c3d2e1f0a9b8c7d6e5f4a3b2c";
        using var app = AppSession.Start(new Scenario()
            .Process(p => p
                .Emit(
                    "ERROR:  Gateway certificate validation failed, and the certificate digest is not in the local whitelist. If you trust it, rerun with:",
                    $"ERROR:      --trusted-cert {digest}")
                .Exit(1))
            .Process(p => p.Emit(Scenario.TunnelUp))
            .Process(p => p.Emit(Scenario.TunnelUp)));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.DialogButton("Trust and connect").Click();
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
        app.Tap("DisconnectButton");
        app.WaitForText("StatusLabel", "Not connected");

        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");

        Assert.Equal([$"2 {digest}", $"3 {digest}"], app.TrustedCertsPassed);
    }

    [Fact]
    public void Fills_in_the_saved_password()
    {
        using var app = AppSession.Start(new Scenario().Process(p => p.Emit(Scenario.TunnelUp)), savedPassword: "from-keychain");

        // The field is a password field, so its text is not readable; connecting without typing proves it.
        app.WaitForText("StatusLabel", "Not connected");
        app.Tap("ConnectButton");

        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
    }
}
