using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Wallup.Interop;

namespace Wallup.Views;

/// <summary>
/// A pill of clear glass lying on the wallpaper.
///
/// The illusion is built, not blurred for free by the system. DWM's acrylic can only frost
/// a rectangle in the system's own tint, and it refuses to run at all on a per-pixel-alpha
/// window - which is the only kind that can be capsule-shaped. So the capsule paints the
/// wallpaper itself: it works out which patch of the picture it is covering and draws that
/// patch back, slightly magnified and softened, with a much harder magnification in a band
/// around the rim. That band is what a real lens does to whatever lies behind its thick
/// edge, and it is the single cue that makes a flat shape read as glass.
///
/// Because a chip sits directly on the desktop, the patch it paints is exactly what it
/// hides, so the glass is genuinely see-through even though the window is opaque.
/// </summary>
internal sealed class GlassCapsule : ContentControl
{
    /// <summary>How far the refracted picture spills past the capsule, so blur has material.</summary>
    private const double Bleed = 24;

    /// <summary>Magnification of the picture seen through the middle of the glass.</summary>
    private const double CoreZoom = 1.08;

    /// <summary>Magnification in the rim band. Far stronger: this is the thick edge.</summary>
    private const double EdgeZoom = 1.75;

    /// <summary>Roundest a corner is allowed to get, so a tall panel stays a card.</summary>
    private const double MaxRadius = 28;

    private ImageBrush? _core;
    private ImageBrush? _edge;
    private Border? _shadow;
    private FrameworkElement? _clip;
    private Window? _window;
    private Rect _lastScreenRect = Rect.Empty;

    internal GlassCapsule()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => Refresh();
    }

    public static readonly DependencyProperty CornerCurveProperty =
        DependencyProperty.Register(
            nameof(CornerCurve), typeof(CornerRadius), typeof(GlassCapsule),
            new FrameworkPropertyMetadata(new CornerRadius(18)));

    /// <summary>
    /// The capsule's roundness, recomputed from the height so the ends stay semicircular.
    /// The template binds to this rather than to CornerRadius, which ContentControl does
    /// not have.
    /// </summary>
    public CornerRadius CornerCurve
    {
        get => (CornerRadius)GetValue(CornerCurveProperty);
        set => SetValue(CornerCurveProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _clip = GetTemplateChild("PART_Clip") as FrameworkElement;
        _shadow = GetTemplateChild("PART_Shadow") as Border;

        _core = NewBrush();
        _edge = NewBrush();

        if (GetTemplateChild("PART_Core") is Shape core)
        {
            core.Fill = _core;
        }

        if (GetTemplateChild("PART_Edge") is Border edge)
        {
            edge.BorderBrush = _edge;
        }

        // Nothing to refract: let the wash carry the glass on its own rather than
        // showing the shadow plate through an empty capsule.
        if (_shadow is not null && Wallpaper.Image is null)
        {
            _shadow.Visibility = Visibility.Collapsed;
        }

        Refresh();
    }

    private static ImageBrush NewBrush() => new(Wallpaper.Image)
    {
        Stretch = Stretch.Fill,
        ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
    };

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);

        if (_window is not null)
        {
            _window.LocationChanged += OnWindowMoved;
        }

        Wallpaper.Changed += OnWallpaperChanged;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Wallpaper.Changed -= OnWallpaperChanged;

        if (_window is not null)
        {
            _window.LocationChanged -= OnWindowMoved;
            _window = null;
        }
    }

    private void OnWindowMoved(object? sender, EventArgs e) => Refresh();

    /// <summary>The picture we are refracting has been replaced, so pick up the new one.</summary>
    private void OnWallpaperChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (_core is null || _edge is null)
        {
            return;
        }

        _core.ImageSource = Wallpaper.Image;
        _edge.ImageSource = Wallpaper.Image;

        // A new picture means a new brightness underneath, so the tone is up for grabs too.
        if (_window is not null)
        {
            AdaptiveGlass.Apply(_window);
        }

        _lastScreenRect = Rect.Empty;
        Refresh();
    });

    /// <summary>
    /// Re-reads the patch of wallpaper under the capsule. Cheap enough to call on every
    /// step of a drag: it only moves two brush viewboxes, it does not touch the bitmap.
    /// </summary>
    internal void Refresh()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var radius = Math.Min(Math.Min(ActualHeight, ActualWidth) / 2, MaxRadius);
        CornerCurve = new CornerRadius(radius);

        _clip?.SetValue(ClipProperty, new RectangleGeometry(
            new Rect(0, 0, ActualWidth, ActualHeight), radius, radius));

        if (!TryScreenRect(out var onScreen) || onScreen == _lastScreenRect)
        {
            return;
        }

        _lastScreenRect = onScreen;

        // The refracted layers are drawn on a rectangle inflated by the bleed, so their
        // viewboxes have to cover that larger patch of wallpaper too.
        var bleed = Bleed * onScreen.Width / Math.Max(ActualWidth, 1);
        var painted = Rect.Inflate(onScreen, bleed, bleed);

        Set(_core, painted, CoreZoom);
        Set(_edge, painted, EdgeZoom);
    }

    /// <summary>Points the brush at the wallpaper behind <paramref name="painted"/>, zoomed.</summary>
    private static void Set(ImageBrush? brush, Rect painted, double zoom)
    {
        if (brush?.ImageSource is null)
        {
            return;
        }

        // Zooming in means reading a smaller patch and stretching it over the same area,
        // shrunk about the centre so the middle of the glass stays put.
        var box = Wallpaper.ViewboxFor(painted);

        brush.Viewbox = new Rect(
            box.X + box.Width * (1 - 1 / zoom) / 2,
            box.Y + box.Height * (1 - 1 / zoom) / 2,
            box.Width / zoom,
            box.Height / zoom);
    }

    /// <summary>The capsule's own rectangle in physical screen pixels.</summary>
    private bool TryScreenRect(out Rect rect)
    {
        rect = Rect.Empty;

        if (PresentationSource.FromVisual(this) is null)
        {
            return false;
        }

        try
        {
            rect = new Rect(
                PointToScreen(new Point(0, 0)),
                PointToScreen(new Point(ActualWidth, ActualHeight)));

            return true;
        }
        catch (InvalidOperationException)
        {
            return false; // no longer connected to a window
        }
    }
}
