using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

using Wallup.Diagnostics;

namespace Wallup.Interop;

/// <summary>
/// Real Windows 11 acrylic, via DWM rather than a fake translucent brush.
///
/// This only works on a window that is NOT layered, so these windows use
/// <c>AllowsTransparency="False"</c> with a transparent WPF background. Turning on WPF
/// transparency would make the window layered and DWM would refuse to draw a backdrop
/// behind it, which is the usual reason acrylic silently does nothing.
/// </summary>
internal static class Glass
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;

    private const int CornerRound = 2;
    private const int CornerRoundSmall = 3;

    /// <summary>Acrylic: blurs whatever is behind the window, including the wallpaper.</summary>
    private const int BackdropTransientWindow = 3;

    /// <summary>
    /// Applies acrylic, dark mode and rounded corners. Call after the window has a handle,
    /// which means from OnSourceInitialized or later.
    /// </summary>
    /// <summary>
    /// Applies acrylic and rounded corners.
    /// </summary>
    /// <param name="window">The window to frost. Must already have a handle.</param>
    /// <param name="smallCorners">Tighter corner radius, for small surfaces.</param>
    /// <param name="light">
    /// Light acrylic, which frosts toward white the way macOS vibrancy does, rather than
    /// the dark smoked tint Windows defaults to. The content on top must use dark ink.
    /// </param>
    internal static void Apply(Window window, bool smallCorners = false, bool light = true)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // Without a transparent background WPF paints over the backdrop DWM provides.
        window.Background = System.Windows.Media.Brushes.Transparent;

        // The immersive mode flag decides which way the acrylic tints. Off means the
        // frost pulls toward white instead of black.
        var dark = light ? 0 : 1;
        var darkResult = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

        var backdrop = BackdropTransientWindow;
        var backdropResult = DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref backdrop, sizeof(int));

        var corner = smallCorners ? CornerRoundSmall : CornerRound;
        var cornerResult = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref corner, sizeof(int));

        // A negative margin extends the glass across the whole client area.
        var margins = new MARGINS { Left = -1, Top = -1, Right = -1, Bottom = -1 };
        var frameResult = DwmExtendFrameIntoClientArea(handle, ref margins);

        // Acrylic is best-effort: older builds simply refuse the attribute. Record it
        // rather than leaving a blank window with no explanation.
        if (backdropResult != 0 || frameResult != 0)
        {
            Log.Warn($"Acrylic unavailable (backdrop=0x{backdropResult:X}, frame=0x{frameResult:X}, " +
                     $"dark=0x{darkResult:X}, corner=0x{cornerResult:X}); falling back to a solid panel.");

            window.Background = new System.Windows.Media.SolidColorBrush(
                light
                    ? System.Windows.Media.Color.FromArgb(0xF2, 0xF4, 0xF6, 0xFA)
                    : System.Windows.Media.Color.FromArgb(0xE6, 0x10, 0x12, 0x16));
        }
    }
}
