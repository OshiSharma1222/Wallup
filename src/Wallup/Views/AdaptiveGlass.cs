using System.Windows;
using System.Windows.Interop;
using Wallup.Diagnostics;
using Wallup.Interop;

namespace Wallup.Views;

/// <summary>
/// Picks dark or light glass for a window based on how bright the wallpaper is behind it,
/// then merges the matching palette into that window's own resources.
///
/// Per window rather than per app: a chip on the bright part of a wallpaper should go
/// light while one on the dark part goes dark, the way macOS vibrancy does.
/// </summary>
internal static class AdaptiveGlass
{
    /// <summary>Applies the palette and the DWM backdrop. Call from OnSourceInitialized.</summary>
    internal static bool Apply(Window window, bool smallCorners = false)
    {
        var light = IsOverLightWallpaper(window);

        var palette = new ResourceDictionary
        {
            Source = new Uri(
                light ? "Views/GlassLight.xaml" : "Views/GlassDark.xaml",
                UriKind.Relative),
        };

        // Window-level resources win over the app-level ones for DynamicResource lookups,
        // so this repaints everything inside without touching any other window.
        window.Resources.MergedDictionaries.Add(palette);

        Glass.Apply(window, smallCorners, light);
        return light;
    }

    private static bool IsOverLightWallpaper(Window window)
    {
        try
        {
            var source = PresentationSource.FromVisual(window) as HwndSource;
            var transform = source?.CompositionTarget?.TransformToDevice;

            var scaleX = transform?.M11 ?? 1.0;
            var scaleY = transform?.M22 ?? 1.0;

            // The sampler works in physical pixels; WPF's Left/Top are device-independent.
            var x = (int)(window.Left * scaleX);
            var y = (int)(window.Top * scaleY);
            var width = (int)(Math.Max(window.Width, 1) * scaleX);
            var height = (int)(Math.Max(window.ActualHeight, 40) * scaleY);

            var screenWidth = (int)(SystemParameters.PrimaryScreenWidth * scaleX);
            var screenHeight = (int)(SystemParameters.PrimaryScreenHeight * scaleY);

            var luminance = WallpaperSampler.LuminanceAt(x, y, width, height, screenWidth, screenHeight);
            var light = luminance > WallpaperSampler.LightThreshold;

            Log.Info($"Glass for \"{window.Title}\" at {x},{y}: wallpaper luminance " +
                     $"{luminance:F3} -> {(light ? "light" : "dark")}.");

            return light;
        }
        catch (InvalidOperationException ex)
        {
            Log.Warn($"Could not decide glass tone, defaulting to dark: {ex.Message}");
            return false;
        }
    }
}
