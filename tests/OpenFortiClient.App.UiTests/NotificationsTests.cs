namespace OpenFortiClient.App.UiTests;

[Collection(UiCollection.Name)]
public sealed class NotificationsTests
{
    [Fact]
    [Trait("Suite", "Quick")]
    public void Records_connect_drop_and_errors_in_the_history()
    {
        // The drop is the case that used to go missing: it showed only as a later "Disconnected.".
        using var app = AppSession.Start(new Scenario()
            .Process(p => p.Emit(Scenario.TunnelUp).WaitFor("drop").Emit("INFO:   Cancelling threads...")));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway1}");
        app.Trigger("drop");
        app.WaitForText("ErrorLabel", "was lost");

        app.Titled("Notifications").Click();

        var messages = AppSession.WaitFor(() =>
        {
            var texts = app.Texts("NotificationMessage");
            return texts.Count >= 2 ? texts : null;
        }) ?? [];
        Assert.Equal([$"The connection to {AppSession.Gateway1} was lost.", $"Connected to {AppSession.Gateway1}."], messages);
    }

    [Fact]
    public void Shows_a_failover_as_a_warning()
    {
        using var app = AppSession.Start(new Scenario()
            .Process(p => p.Emit("ERROR:  connect: Connection refused").Exit(1))
            .Process(p => p.Emit(Scenario.TunnelUp)));

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", $"Connected to {AppSession.Gateway2}");

        app.Titled("Notifications").Click();

        var severities = AppSession.WaitFor(() => app.Texts("NotificationSeverity") is { Count: >= 2 } t ? t : null) ?? [];
        var messages = app.Texts("NotificationMessage");
        Assert.Equal(["Info", "Warning"], severities);
        Assert.Equal($"Connected to {AppSession.Gateway2}.", messages[0]);
        Assert.StartsWith($"{AppSession.Gateway1} failed (", messages[1]);
    }

    [Fact]
    public void Records_a_cancelled_attempt_as_cancelled_not_as_disconnected()
    {
        // A gateway that never answers, with a long timeout, so the attempt is still running when it is cancelled.
        using var app = AppSession.Start(new Scenario().Process(_ => { }), settings: s => s["gatewayTimeoutSeconds"] = 120);

        app.Type("PasswordEntry", "s3cret");
        app.Tap("ConnectButton");
        app.WaitForText("StatusLabel", "Connecting");
        app.Tap("DisconnectButton");
        app.WaitForText("StatusLabel", "Not connected");

        app.Titled("Notifications").Click();

        var messages = AppSession.WaitFor(() => app.Texts("NotificationMessage") is { Count: >= 1 } t ? t : null) ?? [];
        Assert.Equal(["Connection attempt cancelled."], messages);
    }
}
