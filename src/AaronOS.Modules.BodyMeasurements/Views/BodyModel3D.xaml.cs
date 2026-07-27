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

    /// <summary>Which measurement each mesh stands for, so a hit can be routed to a field.</summary>
    private readonly Dictionary<GeometryModel3D, GoalMetric?> _partsByModel = [];

    private Point _dragOrigin;
    private double _dragDistance;
    private bool _dragging;
    private double _heightInches = 70;

    /// <summary>Raised when a body part is clicked (as opposed to dragged) with a part that maps to a
    /// measurement.</summary>
    public event Action<GoalMetric>? PartClicked;

    public BodyModel3D()
    {
        InitializeComponent();
        Cursor = Cursors.Hand;
        Apply(null, null);
    }

    /// <summary>Rebuilds the figure. Pass a null check-in to show the dimmed average reference.</summary>
    public void Apply(BodyCheckIn? checkIn, decimal? heightInches)
    {
        var measurements = FigureMeasurements.FromCheckIn(checkIn, heightInches);
        var hasData = FigureMeasurements.HasAnyMeasurement(checkIn);

        _heightInches = measurements.HeightInches;
        NoDataLabel.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;

        _partsByModel.Clear();
        var figure = new Model3DGroup();
        foreach (var (model, metric) in BodyFigureBuilder.Build(measurements, CreateMaterial(hasData)))
        {
            figure.Children.Add(model);
            _partsByModel[model] = metric;
        }

        // Orbit is applied to the whole figure, pivoting about its middle so it turns on the spot
        // rather than swinging away from the camera.
        var pivot = _heightInches * 0.5;
        var transforms = new Transform3DGroup();
        transforms.Children.Add(new TranslateTransform3D(0, -pivot, 0));
        transforms.Children.Add(new RotateTransform3D(_pitch));
        transforms.Children.Add(new RotateTransform3D(_yaw));
        transforms.Children.Add(new TranslateTransform3D(0, pivot, 0));
        figure.Transform = transforms;

        FigureVisual.Content = figure;
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

    private void PositionCamera()
    {
        // Framed from the figure's own height so a tall and a short person both fill the panel.
        var target = new Point3D(0, _heightInches * 0.50, 0);
        Camera.Position = new Point3D(0, target.Y + _heightInches * 0.02, _heightInches * 1.62);
        Camera.LookDirection = target - Camera.Position;
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
        if (VisualTreeHelper.HitTest(Viewport, position) is not RayMeshGeometry3DHitTestResult hit
            || hit.ModelHit is not GeometryModel3D model
            || !_partsByModel.TryGetValue(model, out var metric)
            || metric is null)
        {
            return;
        }

        // The torso is a single mesh spanning several measurements, so resolve it by where on the body
        // the ray actually landed.
        var resolved = metric == GoalMetric.Chest
            ? BodyFigureBuilder.ResolveTorsoPart(hit.PointHit.Y, _heightInches)
            : metric.Value;

        PartClicked?.Invoke(resolved);
    }
}
