using AaronOS.Modules.BodyMeasurements.Data;
using AaronOS.Modules.BodyMeasurements.Views.Body3D;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AaronOS.Modules.BodyMeasurements.Views;

/// <summary>
/// A rotatable 3D figure whose proportions come from the latest check-in — the character-selector view
/// of your own measurements. Drag to orbit, click a body part to edit that measurement.
///
/// It always renders something: with nothing recorded it shows average proportions, dimmed and
/// labelled, so the model is a frame of reference from the first launch rather than an empty panel.
/// </summary>
public partial class BodyModel3D : UserControl
{
    private const double MaxPitch = 20;
    private const double ClickSlopPixels = 4;

    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 20);
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), 0);

    private Point _dragOrigin;
    private double _dragDistance;
    private bool _dragging;
    private double _heightInches = 70;

    // 1.0 frames the whole figure; below that moves in, above pulls back.
    private double _zoom = 1.0;
    private const double MinZoom = 0.32;
    private const double MaxZoom = 1.8;

    /// <summary>How much of the panel's height the whole figure takes at zoom 1.0, leaving a margin so
    /// the head and feet are not flush against the edges.</summary>
    private const double FillFraction = 0.88;

    /// <summary>Raised when a body part is clicked (as opposed to dragged) with a part that maps to a
    /// measurement.</summary>
    public event Action<GoalMetric>? PartClicked;

    public BodyModel3D()
    {
        InitializeComponent();
        Cursor = Cursors.Hand;

        // The framing depends on the panel's shape, so it has to be recomputed whenever that changes.
        Viewport.SizeChanged += (_, _) => PositionCamera();

        Apply(null, null);
    }

    /// <summary>Rebuilds the figure. Pass a null check-in to show the dimmed average reference.</summary>
    public void Apply(BodyCheckIn? checkIn, decimal? heightInches)
    {
        var measurements = FigureMeasurements.FromCheckIn(checkIn, heightInches);
        var hasData = FigureMeasurements.HasAnyMeasurement(checkIn);

        _heightInches = measurements.HeightInches;
        NoDataLabel.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;

        var material = CreateMaterial(hasData);

        // Orbit lives on the visual, not on the model. A hit test reports its point in the containing
        // Visual3D's coordinate space, so keeping the rotation above the model means clicks arrive in
        // the figure's own upright space and can be matched against anatomy directly.
        var pivot = _heightInches * 0.5;
        var transforms = new Transform3DGroup();
        transforms.Children.Add(new TranslateTransform3D(0, -pivot, 0));
        transforms.Children.Add(new RotateTransform3D(_pitch));
        transforms.Children.Add(new RotateTransform3D(_yaw));
        transforms.Children.Add(new TranslateTransform3D(0, pivot, 0));
        FigureVisual.Transform = transforms;

        FigureVisual.Content = new GeometryModel3D(BodyMeshDeformer.Build(measurements), material)
        {
            // Set even though the mesh is closed and correctly wound: without it, a single inverted
            // triangle would show as a hole straight through the figure.
            BackMaterial = material,
        };
        PositionCamera();
    }

    /// <summary>
    /// A neutral clay, the way a character editor presents an unskinned base mesh — it shows form
    /// without pretending to be skin. Slightly translucent and desaturated with no data, so the
    /// reference figure reads as a placeholder without disappearing.
    /// </summary>
    private static Material CreateMaterial(bool hasData)
    {
        var body = new SolidColorBrush(hasData
            ? Color.FromRgb(0xC6, 0xCF, 0xD8)
            : Color.FromRgb(0x6A, 0x76, 0x84))
        {
            Opacity = hasData ? 1.0 : 0.5
        };

        return new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(body),
                // Broad and dim rather than a tight hotspot: a wide falloff reads as soft clay, while
                // a sharp highlight makes the same mesh look like moulded plastic.
                new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x40, 0xD8, 0xEC, 0xFF)), 12),
            }
        };
    }

    /// <summary>
    /// Frames the figure from its own height, so a tall and a short person both fill the panel, and
    /// from the panel's current shape.
    ///
    /// The aspect term is the part that matters: WPF's <see cref="PerspectiveCamera.FieldOfView"/> is
    /// the <em>horizontal</em> angle, so the vertical angle — the one that decides whether a standing
    /// figure fits — changes as the window is resized. Deriving the distance from the live viewport
    /// size keeps the framing steady instead of cropping the head on a wide window.
    /// </summary>
    private void PositionCamera()
    {
        var aspect = Viewport.ActualWidth > 0 && Viewport.ActualHeight > 0
            ? Viewport.ActualHeight / Viewport.ActualWidth
            : 1.6;

        var verticalHalfTan = Math.Tan(Camera.FieldOfView * Math.PI / 360) * aspect;
        var distance = _heightInches / (2 * verticalHalfTan * FillFraction) * _zoom;

        var target = new Point3D(0, _heightInches * 0.50, 0);
        Camera.Position = new Point3D(0, target.Y + (_heightInches * 0.02), distance);
        Camera.LookDirection = target - Camera.Position;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var previous = _zoom;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 0.90 : 1.0 / 0.90), MinZoom, MaxZoom);

        if (Math.Abs(_zoom - previous) < 1e-9)
        {
            // Already at the near or far limit: leave the event unhandled so the wheel falls through
            // to the page scroller instead of feeling stuck.
            return;
        }

        PositionCamera();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragOrigin = e.GetPosition(this);
        _dragDistance = 0;
        _dragging = true;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        var current = e.GetPosition(this);
        var dx = current.X - _dragOrigin.X;
        var dy = current.Y - _dragOrigin.Y;
        _dragDistance += Math.Abs(dx) + Math.Abs(dy);

        _yaw.Angle += dx * 0.45;
        // Pitch is clamped: letting it pass vertical flips the figure and is disorienting.
        _pitch.Angle = Math.Clamp(_pitch.Angle + dy * 0.22, -MaxPitch, MaxPitch);
        _dragOrigin = current;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();

        // Rotating the model should not also open an editor, so only a press that barely moved counts
        // as a click on a body part.
        if (_dragDistance <= ClickSlopPixels)
        {
            RaiseHitPart(e.GetPosition(Viewport));
        }
    }

    private void RaiseHitPart(Point position)
    {
        if (VisualTreeHelper.HitTest(Viewport, position) is not RayMeshGeometry3DHitTestResult hit)
        {
            return;
        }

        // The figure is one continuous mesh, so which measurement was clicked comes from where on the
        // body the ray landed. PointHit is in the mesh's own space, which is the undeformed pose in
        // inches — the orbit rotation sits on the group above it and so does not interfere.
        if (BodyMeshDeformer.ResolveMetric(hit.PointHit, _heightInches) is { } metric)
        {
            PartClicked?.Invoke(metric);
        }
    }
}
