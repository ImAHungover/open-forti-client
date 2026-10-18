namespace OpenFortiClient.App.UiTests;

/// <summary>Adding, editing and deleting connections through the "≡" menu.</summary>
[Collection(UiCollection.Name)]
public sealed class ConnectionsTests
{
    [Fact]
    [Trait("Suite", "Quick")]
    public void Adds_a_connection_and_connects_with_it()
    {
        using var app = AppSession.Start(new Scenario().Process(p => p.Emit(Scenario.TunnelUp)), withProfile: false);
        app.Element("EmptyHintLabel");

        app.Tap("ManageConnectionsButton");
        app.DialogButton("Add a new connection").Click();
        app.Type("NameEntry", "Office");
        app.Type("GatewayEntry", "https://vpn.office.example:443");
        app.Type("EditUsernameEntry", "jane");
        app.Tap("SaveButton");

        app.WaitForText("ProfilePicker", "Office");
        Assert.Contains("\"name\": \"Office\"", app.ProfilesJson);

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", "Connected to vpn.office.example");
    }

    [Fact]
    public void Deletes_a_connection_after_confirmation()
    {
        using var app = AppSession.Start(new Scenario());
        app.WaitForText("ProfilePicker", "Test VPN");

        app.Tap("ManageConnectionsButton");
        app.DialogButton("Delete the selected connection").Click();
        app.DialogButton("Delete").Click(); // the confirmation

        app.Element("EmptyHintLabel");
        Assert.DoesNotContain("Test VPN", app.ProfilesJson);
    }

    [Fact]
    public void Renaming_a_connection_keeps_its_saved_password()
    {
        using var app = AppSession.Start(new Scenario().Process(p => p.Emit(Scenario.TunnelUp)), savedPassword: "from-keychain");
        app.WaitForText("ProfilePicker", "Test VPN");

        app.Tap("ManageConnectionsButton");
        app.DialogButton("Edit the selected connection").Click();
        app.Type("NameEntry", "Renamed VPN", replace: true);
        app.Tap("SaveButton");
        app.WaitForText("ProfilePicker", "Renamed VPN");

        app.Tap("ConnectButton"); // no password typed: it must come from the saved one
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
    }

    [Fact]
    public void Switching_to_prompt_on_login_forgets_the_saved_password()
    {
        using var app = AppSession.Start(new Scenario(), savedPassword: "from-keychain");
        app.WaitForText("ProfilePicker", "Test VPN");
        Assert.Single(app.Secrets);

        app.Tap("ManageConnectionsButton");
        app.DialogButton("Edit the selected connection").Click();
        app.Tap("SaveLoginCheckBox");
        app.Tap("SaveButton");
        app.Element("ConnectButton");

        Assert.Empty(AppSession.WaitFor(() => app.Secrets.Count == 0 ? app.Secrets : null) ?? app.Secrets);
        Assert.Contains("\"authMode\": \"PromptOnLogin\"", app.ProfilesJson);
    }

    [Fact]
    public void Removes_a_trusted_gateway_certificate()
    {
        const string digest = "4daa8c5fe6c8b1a1f0e3c2d4b5a69788796a5b4c3d2e1f0a9b8c7d6e5f4a3b2c";
        using var app = AppSession.Start(new Scenario(), profile: p => p["trustedCertDigests"] = new System.Text.Json.Nodes.JsonArray(digest));
        app.WaitForText("ProfilePicker", "Test VPN");

        app.Tap("ManageConnectionsButton");
        app.DialogButton("Edit the selected connection").Click();
        app.WaitForText("TrustedCertLabel", "4daa8c5fe6c8b1a1");
        app.Tap("RemoveTrustedCertButton");
        app.Element("NoTrustedCertsLabel");
        app.Tap("SaveButton");
        app.Element("ConnectButton");

        Assert.DoesNotContain(digest, app.ProfilesJson);
    }

    [Fact]
    public void Refuses_a_client_certificate_that_does_not_exist()
    {
        using var app = AppSession.Start(new Scenario());
        app.WaitForText("ProfilePicker", "Test VPN");

        app.Tap("ManageConnectionsButton");
        app.DialogButton("Edit the selected connection").Click();
        app.Type("ClientCertificateEntry", "/nonexistent/cert.pem");
        app.Type("ClientKeyEntry", "/nonexistent/key.pem");
        app.Tap("SaveButton");

        Assert.Contains("Client certificate file not found: /nonexistent/cert.pem", app.DialogText());
        app.DialogButton("OK").Click();
        Assert.DoesNotContain("/nonexistent/cert.pem", app.ProfilesJson);
    }

    [Fact]
    public void A_client_key_file_moves_into_the_keychain_and_is_used_to_connect()
    {
        using var app = AppSession.Start(new Scenario().Process(p => p.Emit(Scenario.TunnelUp)));
        var cert = Path.Combine(app.Directory, "me.crt");
        var key = Path.Combine(app.Directory, "me.key");
        File.WriteAllText(cert, "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n");
        File.WriteAllText(key, "-----BEGIN PRIVATE KEY-----\nMIIE\n-----END PRIVATE KEY-----\n");
        File.SetUnixFileMode(key, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        app.WaitForText("ProfilePicker", "Test VPN");

        app.Tap("ManageConnectionsButton");
        app.DialogButton("Edit the selected connection").Click();
        app.Type("ClientCertificateEntry", cert);
        app.Type("ClientKeyEntry", key);
        app.Tap("SaveButton");
        app.Element("ConnectButton");

        var json = AppSession.WaitFor(() => app.ProfilesJson.Contains("\"clientKeyInKeychain\": true") ? app.ProfilesJson : null) ?? app.ProfilesJson;
        Assert.Contains("\"clientKeyInKeychain\": true", json);
        Assert.DoesNotContain(key, json);
        Assert.Contains(app.Secrets, s => s.Contains("/client-key\t"));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
        Assert.Equal(["1 -----BEGIN PRIVATE KEY-----"], app.ClientKeysPassed);
    }

    [Fact]
    public void Deleting_a_connection_removes_its_client_key_from_the_keychain()
    {
        using var app = AppSession.Start(new Scenario(), profile: p =>
        {
            p["clientCertificatePath"] = "/certs/me.crt";
            p["clientKeyInKeychain"] = true;
        });
        File.AppendAllText(Path.Combine(app.Directory, "secrets.txt"),
            $"profile/{AppSession.ProfileId.Replace("-", "")}/client-key\t{Convert.ToBase64String("-----BEGIN PRIVATE KEY-----"u8.ToArray())}\n");
        app.WaitForText("ProfilePicker", "Test VPN");

        app.Tap("ManageConnectionsButton");
        app.DialogButton("Delete the selected connection").Click();
        app.DialogButton("Delete").Click();
        app.Element("EmptyHintLabel");

        Assert.Empty(AppSession.WaitFor(() => app.Secrets.Count == 0 ? app.Secrets : null) ?? app.Secrets);
    }
}
