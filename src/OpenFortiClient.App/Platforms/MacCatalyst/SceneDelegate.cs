using Foundation;

namespace OpenFortiClient.App;

/// <summary>UIKit refuses to launch apps built with the macOS 27 SDK that do not use the scene life cycle.</summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}
