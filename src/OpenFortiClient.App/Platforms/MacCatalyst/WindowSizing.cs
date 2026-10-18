using CoreGraphics;
using OpenFortiClient.Core.Logging;
using UIKit;

namespace OpenFortiClient.App;

/// <summary>
/// The window's size: the Settings page (the largest) plus about 1 cm each way, not full screen.
/// Mac Catalyst ignores MAUI's Window.Width/Height (the window opened at 1330 × 876) and keeps a restored
/// size over a requested one, so the size limits are pinned to the size for a moment, which forces it.
/// Sizes are UIKit points: a Catalyst app in iPad style is drawn at 77 % (<see cref="MacScale"/>), so on
/// screen this is about 641 × 884 points, where 1 cm is about 50 points.
/// </summary>
public static class WindowSizing
{
    /// <summary>"Scale Interface to Match iPad": one UIKit point is drawn as 0.77 Mac points.</summary>
    public const double MacScale = 0.77;

    /// <summary>Settings: 720 + 2 × 24 padding wide, about 1083 tall; plus 65 (≈ 1 cm on screen) each way.</summary>
    public static readonly CGSize Default = new(833, 1148);

    public static readonly CGSize Minimum = new(700, 760);

    public static void Apply(UIWindowScene scene, IAppLog log)
    {
        if (scene.SizeRestrictions is not { } limits)
        {
            return;
        }

        // Never taller than what is free below the menu bar (screen bounds are Mac points here).
        var screen = scene.Screen.Bounds;
        var size = new CGSize(
            Math.Min(Default.Width, screen.Width / MacScale),
            Math.Min(Default.Height, (screen.Height - 70) / MacScale));

        limits.MinimumSize = size;
        limits.MaximumSize = size;
        _ = Task.Delay(500).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            limits.MinimumSize = new CGSize(Math.Min(Minimum.Width, size.Width), Math.Min(Minimum.Height, size.Height));
            limits.MaximumSize = new CGSize(nfloat.MaxValue, nfloat.MaxValue);
        }));
    }
}
