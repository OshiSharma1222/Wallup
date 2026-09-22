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
    private const string DarkPalette = "Views/GlassDark.xaml";
    private const string LightPalette = "Views/GlassLight.xaml";

    /// <summary>
    /// Applies the palette for wherever the window currently sits. Safe to call again
    /// after the window moves; it swaps the palette rather than stacking a second one.
    /// Call it once the window has a handle AND its final position, because sampling
    /// before the move reads the wallpaper under the old spot.
    /// </summary>
    /// <param name="window">The window to tone.</param>
    /// <param name="acrylic">
    /// Also ask DWM for a blurred backdrop. Only for windows that float over other apps,
    /// where the wallpaper is not what is behind them; glass on the desktop layer refracts
    /// the wallpaper itself instead. See <see cref="GlassCapsule"/>.
    /// </param>
    internal static bool Apply(Window window, bool acrylic = false)
    {
        var light = IsOverLightWallpaper(window);
        var wanted = light ? LightPalette : DarkPalette;

        var merged = window.Resources.MergedDictionaries;
        var current = merged.FirstOrDefault(d => d.Source is not null && IsPalette(d.Source));

        if (current?.Source is null || current.Source.OriginalString != wanted)
        {
            if (current is not null)
            {
                merged.Remove(current);
            }

            // Window-level resources win over the app-level ones for DynamicResource
            // lookups, so this repaints everything inside without touching any other
            // window.
            merged.Add(new ResourceDictionary { Source = new Uri(wanted, UriKind.Relative) });
        }

        if (acrylic)
        {
            Glass.Apply(window, light: light);
        }

        return light;
    }

    private static bool IsPalette(Uri source) =>
        source.OriginalString is DarkPalette or LightPalette;

    private static bool IsOverLightWallpaper(Window window)
    {
        try
        {
            var source = PresentationSource.FromVisual(window) as HwndSource;
            var transform = source?.CompositionTarget?.TransformToDevice;

            var scaleX = transform?.M11 ?? 1.0;
            var scaleY = transform?.M22 ?? 1.0;

            // The wallpaper is measured in physical pixels; WPF's Left/Top are not.
            var patch = new Rect(
                window.Left * scaleX,
                window.Top * scaleY,
                Math.Max(window.Width, 1) * scaleX,
                Math.Max(window.ActualHeight, 40) * scaleY);

            var luminance = Wallpaper.LuminanceAt(patch);
            var light = luminance > Wallpaper.LightThreshold;

            Log.Info($"Glass for \"{window.Title}\" at {patch.X:F0},{patch.Y:F0}: wallpaper " +
                     $"luminance {luminance:F3} -> {(light ? "light" : "dark")}.");

            return light;
        }
        catch (InvalidOperationException ex)
        {
            Log.Warn($"Could not decide glass tone, defaulting to dark: {ex.Message}");
            return false;
        }
    }
}
