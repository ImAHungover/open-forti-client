using System.Drawing;
using OpenQA.Selenium.Appium;

namespace OpenFortiClient.App.UiTests;

[Collection(UiCollection.Name)]
public sealed class WindowTests
{
    [Fact]
    [Trait("Suite", "Quick")]
    public void Opens_just_big_enough_for_the_settings_and_edit_pages()
    {
        using var app = AppSession.Start(new Scenario());
        // Not full screen: about the Settings page's width plus 1 cm (≈ 50 points on screen). The size is
        // forced right after the window appears.
        var window = AppSession.WaitFor(() =>
            app.Driver.FindElement(MobileBy.AccessibilityId("SceneWindow")).Rect is { Width: >= 600 and <= 700 } r ? (object)r : null) as Rectangle?;
        Assert.NotNull(window);

        app.Titled("Settings").Click();
        AssertInside(window.Value, app.Element("SaveSettingsButton").Rect, "Settings → Save settings");

        app.Titled("Connect").Click();
        app.Tap("ManageConnectionsButton");
        app.DialogButton("Edit the selected connection").Click();
        AssertInside(window.Value, app.Element("SaveButton").Rect, "Edit → Save");
    }

    private static void AssertInside(Rectangle window, Rectangle element, string what) =>
        Assert.True(window.Contains(element), $"{what} ({element}) is not fully inside the window ({window}) without scrolling.");
}
