namespace OpenFortiClient.App.UiTests;

[Collection(UiCollection.Name)]
public sealed class SmokeTests
{
    [Fact]
    [Trait("Suite", "Quick")]
    public void Starts_with_the_seeded_connection()
    {
        using var app = AppSession.Start(new Scenario());

        Assert.Contains("Not connected", app.WaitForText("StatusLabel", "Not connected"));
        Assert.True(app.IsVisible("ConnectButton"));
    }
}
