using System.Text.RegularExpressions;

namespace OpenFortiClient.App.UiTests;

/// <summary>OpenFortiClient as a ResultLab open-source product: logo, product line, licence.</summary>
[Collection(UiCollection.Name)]
public sealed class BrandingTests
{
    [Fact]
    [Trait("Suite", "Quick")]
    public void Shows_the_resultlab_logo_and_the_licence()
    {
        using var app = AppSession.Start(new Scenario());

        // Catalyst does not put MAUI images in the accessibility tree: the logo is checked in the screenshot.
        app.WaitForText("ProductTagline", "A RESULTLAB OPEN-SOURCE PRODUCT");
        app.SaveScreenshot("connect");

        app.Titled("About").Click();
        app.WaitForText("AboutProduct", "A ResultLab open-source product");
        app.WaitForText("AboutLicense", "MIT License. Copyright © 2026 ResultLab.");
        app.WaitForText("AboutVersion", $"Version {AppVersion()}");
        app.Element("ResultLabProductsButton");
        app.SaveScreenshot("about");

        app.Titled("Settings").Click();
        app.Element("HelperStatusLabel");
        app.SaveScreenshot("settings");
    }

    /// <summary>The version the app is built with (ApplicationDisplayVersion), so a release needs no test change.</summary>
    private static string AppVersion()
    {
        var csproj = File.ReadAllText(Path.Combine(AppSession.RepositoryRoot(), "src", "OpenFortiClient.App", "OpenFortiClient.App.csproj"));
        return Regex.Match(csproj, "<ApplicationDisplayVersion>(.+?)</ApplicationDisplayVersion>").Groups[1].Value;
    }
}
