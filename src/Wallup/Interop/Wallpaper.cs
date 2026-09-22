using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Wallup.Diagnostics;

namespace Wallup.Interop;

/// <summary>
/// The desktop wallpaper, read as a picture rather than as a colour.
///
/// Two things need it. Glass needs the actual pixels sitting behind a window so it can
/// refract them, and the palette needs to know whether that patch is light or dark. Both
/// map a screen rectangle onto the wallpaper the same way, so both live here.
///
/// We read the wallpaper file instead of grabbing the screen: by the time a chip asks what
/// is behind it, it is already on screen and a grab would capture the chip itself.
/// </summary>
internal static class Wallpaper
{
    /// <summary>Above this mean luminance the wallpaper counts as light.</summary>
    internal const double LightThreshold = 0.5;

    /// <summary>Luminance is judged from a thumbnail this wide; a grid is all we need.</summary>
    private const int ThumbnailWidth = 128;

    private static readonly object Gate = new();

    private static BitmapSource? _image;
    private static byte[]? _thumbnail;
    private static int _thumbnailWidth;
    private static int _thumbnailHeight;
    private static bool _loaded;

    static Wallpaper()
    {
        // Fires when the user changes their wallpaper, so glass can re-read it instead of
        // refracting the picture that used to be there.
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General)
            {
                Invalidate();
                Changed?.Invoke();
            }
        };
    }

    /// <summary>Raised after the wallpaper changes, on a system thread.</summary>
    internal static event Action? Changed;

    /// <summary>The wallpaper bitmap, frozen and shareable, or null if it cannot be read.</summary>
    internal static BitmapSource? Image
    {
        get
        {
            lock (Gate)
            {
                Load();
                return _image;
            }
        }
    }

    /// <summary>The primary screen in physical pixels, which is the space Rects here live in.</summary>
    internal static Size ScreenSize => new(
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN));

    /// <summary>
    /// Maps a screen rectangle, in physical pixels, to the matching patch of the wallpaper
    /// in relative (0-1) coordinates - the form an <see cref="ImageBrush"/> viewbox wants.
    /// Returns the whole image when there is nothing to map onto.
    /// </summary>
    internal static Rect ViewboxFor(Rect screen)
    {
        lock (Gate)
        {
            Load();

            if (_image is null)
            {
                return new Rect(0, 0, 1, 1);
            }

            var (scale, offsetX, offsetY) = Fit(_image.PixelWidth, _image.PixelHeight);

            return new Rect(
                (screen.X + offsetX) / scale / _image.PixelWidth,
                (screen.Y + offsetY) / scale / _image.PixelHeight,
                screen.Width / scale / _image.PixelWidth,
                screen.Height / scale / _image.PixelHeight);
        }
    }

    /// <summary>
    /// Mean relative luminance, 0 (black) to 1 (white), of the wallpaper behind a screen
    /// rectangle in physical pixels. Falls back to dark when the wallpaper cannot be read,
    /// because dark glass with light text stays legible on anything.
    /// </summary>
    internal static double LuminanceAt(Rect screen)
    {
        lock (Gate)
        {
            Load();

            if (_thumbnail is null || _thumbnailWidth == 0 || _thumbnailHeight == 0)
            {
                return 0.0;
            }

            var box = ViewboxFor(screen);

            var left = (int)Math.Clamp(box.X * _thumbnailWidth, 0, _thumbnailWidth - 1);
            var top = (int)Math.Clamp(box.Y * _thumbnailHeight, 0, _thumbnailHeight - 1);
            var right = (int)Math.Clamp((box.X + box.Width) * _thumbnailWidth, left + 1, _thumbnailWidth);
            var bottom = (int)Math.Clamp((box.Y + box.Height) * _thumbnailHeight, top + 1, _thumbnailHeight);

            double total = 0;
            var samples = 0;

            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    var i = (y * _thumbnailWidth + x) * 4;

                    // Rec. 709 luma, which tracks perceived brightness far better than a
                    // flat channel average. The buffer is Bgra32.
                    total += (0.0722 * _thumbnail[i] +
                              0.7152 * _thumbnail[i + 1] +
                              0.2126 * _thumbnail[i + 2]) / 255.0;
                    samples++;
                }
            }

            return samples == 0 ? 0.0 : total / samples;
        }
    }

    /// <summary>Forgets the cached picture, so the next read picks up a new wallpaper.</summary>
    internal static void Invalidate()
    {
        lock (Gate)
        {
            _image = null;
            _thumbnail = null;
            _loaded = false;
        }
    }

    /// <summary>Scale and crop offsets for "fill the screen, centred" - the Windows default.</summary>
    private static (double Scale, double OffsetX, double OffsetY) Fit(int imageWidth, int imageHeight)
    {
        var screen = ScreenSize;

        var scale = Math.Max(screen.Width / imageWidth, screen.Height / imageHeight);

        return (scale,
            (imageWidth * scale - screen.Width) / 2.0,
            (imageHeight * scale - screen.Height) / 2.0);
    }

    private static void Load()
    {
        if (_loaded)
        {
            return;
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

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);

                // OnLoad copies the pixels and closes the file, so the user can still
                // change their wallpaper while we are running.
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();

                _image = image;
                BuildThumbnail(image);

                Log.Info($"Wallpaper loaded from {path} ({image.PixelWidth}x{image.PixelHeight}).");
                return;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
            {
                Log.Warn($"Could not read wallpaper at {path}: {ex.Message}");
            }
        }

        Log.Warn("No readable wallpaper; glass will fall back to a flat tint.");
    }

    private static void BuildThumbnail(BitmapSource image)
    {
        var scale = Math.Min(1.0, (double)ThumbnailWidth / image.PixelWidth);

        var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);

        _thumbnailWidth = bgra.PixelWidth;
        _thumbnailHeight = bgra.PixelHeight;
        _thumbnail = new byte[_thumbnailWidth * _thumbnailHeight * 4];

        bgra.CopyPixels(_thumbnail, _thumbnailWidth * 4, 0);
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
}
