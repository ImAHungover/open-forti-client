using Foundation;
using UIKit;

namespace OpenFortiClient.App;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // One window only (its pages are shared); it comes back through the menu bar's "Open OpenFortiClient".
    public override void BuildMenu(IUIMenuBuilder builder)
    {
        base.BuildMenu(builder);
        // The identifier was renamed in Mac Catalyst 26 (macOS 26); macOS 14 and 15 still use the old one.
        var newWindow = OperatingSystem.IsMacCatalystVersionAtLeast(26)
            ? UIMenuIdentifier.NewItem.GetConstant()
#pragma warning disable CA1422 // only reached before Mac Catalyst 26
            : UIMenuIdentifier.NewScene.GetConstant();
#pragma warning restore CA1422
        if (newWindow is not null)
        {
            builder.RemoveMenu(newWindow);
        }
    }
}
