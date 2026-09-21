using System.Drawing;
using System.IO;
using Microsoft.Win32;
using Wallup.Diagnostics;

namespace Wallup.Interop;

/// <summary>
/// Works out how bright the wallpaper is behind a given patch of screen, so a window can
/// pick dark or light glass to sit on it.
///
/// We read the wallpaper bitmap rather than screen-grabbing, because by the time a chip
/// asks the question it is already on screen and would be sampling itself.
/// </summary>
internal static class WallpaperSampler
{
    private static Bitmap? _wallpaper;
    private static bool _loaded;
    private static readonly object Gate = new();

    /// <summary>Above this, the wallpaper counts as light and the glass should go light.</summary>
    internal const double LightThreshold = 0.5;

    /// <summary>
    /// Mean relative luminance, 0 (black) to 1 (white), of the wallpaper behind a screen
    /// rectangle given in physical pixels. Falls back to dark when the wallpaper cannot be
    /// read, because dark glass with light text stays legible on anything.
    /// </summary>
    internal static double LuminanceAt(int x, int y, int width, int height, int screenWidth, int screenHeight)
    {
        var wallpaper = Load();
        if (wallpaper is null || screenWidth <= 0 || screenHeight <= 0)
        {
            return 0.0;
        }

        try
        {
            lock (Gate)
            {
                // Assume the wallpaper is scaled to fill and centre-cropped, which is the
                // Windows default. Other fit modes just make this slightly approximate.
                var scale = Math.Max(
                    (double)screenWidth / wallpaper.Width,
                    (double)screenHeight / wallpaper.Height);

                var offsetX = (wallpaper.Width * scale - screenWidth) / 2.0;
                var offsetY = (wallpaper.Height * scale - screenHeight) / 2.0;

                double total = 0;
                var samples = 0;

                // A coarse grid is plenty; we only need light-or-dark, not a histogram.
                for (var row = 0; row < 6; row++)
                {
                    for (var col = 0; col < 6; col++)
                    {
                        var screenX = x + width * (col + 0.5) / 6.0;
                        var screenY = y + height * (row + 0.5) / 6.0;

                        var srcX = (int)((screenX + offsetX) / scale);
                        var srcY = (int)((screenY + offsetY) / scale);

                        if (srcX < 0 || srcY < 0 || srcX >= wallpaper.Width || srcY >= wallpaper.Height)
                        {
                            continue;
                        }

                        var pixel = wallpaper.GetPixel(srcX, srcY);

                        // Rec. 709 luma, which tracks perceived brightness far better than
                        // a flat channel average.
                        total += (0.2126 * pixel.R + 0.7152 * pixel.G + 0.0722 * pixel.B) / 255.0;
                        samples++;
                    }
                }

                return samples == 0 ? 0.0 : total / samples;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Log.Warn($"Could not sample the wallpaper: {ex.Message}");
            return 0.0;
        }
    }

    private static Bitmap? Load()
    {
        lock (Gate)
        {
            if (_loaded)
            {
                return _wallpaper;
            }

            _loaded = true;

            foreach (var path in CandidatePaths())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        continue;
                    }

                    // Copy into our own bitmap so the file handle does not stay open and
                    // block the user changing their wallpaper.
                    using var source = new Bitmap(path);
                    _wallpaper = new Bitmap(source);

                    Log.Info($"Sampling wallpaper from {path} ({_wallpaper.Width}x{_wallpaper.Height}).");
                    return _wallpaper;
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
                {
                    Log.Warn($"Could not read wallpaper at {path}: {ex.Message}");
                }
            }

            Log.Warn("No readable wallpaper; defaulting to dark glass.");
            return null;
        }
    }

    private static IEnumerable<string?> CandidatePaths()
    {
        string? registryPath = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            registryPath = key?.GetValue("WallPaper") as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not read the wallpaper path: {ex.Message}");
        }

        yield return registryPath;

        // What Windows actually renders, once it has re-encoded the user's picture.
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
    }

    /// <summary>Forgets the cached bitmap, so a wallpaper change is picked up.</summary>
    internal static void Invalidate()
    {
        lock (Gate)
        {
            _wallpaper?.Dispose();
            _wallpaper = null;
            _loaded = false;
        }
    }
}
