using AaronOS.Modules.BodyMeasurements.Data;
using AaronOS.Modules.BodyMeasurements.Views.Body3D;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AaronOS.Modules.BodyMeasurements.Views;

/// <summary>
/// A rotatable 3D figure whose proportions come from the latest check-in — the character-selector
/// view of your own measurements. Drag to orbit.
///
/// It always renders something: with nothing recorded it shows average proportions, dimmed and
/// labelled, so the model is a frame of reference from the first launch rather than an empty panel.
/// </summary>
public partial class BodyModel3D : UserControl
{
    private const double MaxPitch = 22;

    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 18);
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), 0);

    private Point _dragOrigin;
    private bool _dragging;
    private double _heightInches = 70;

    public BodyModel3D()
    {
        InitializeComponent();
        Cursor = Cursors.SizeWE;
        Apply(null, null);
    }

    /// <summary>Rebuilds the figure. Pass a null check-in to show the dimmed average reference.</summary>
    public void Apply(BodyCheckIn? checkIn, decimal? heightInches)
    {
        var measurements = FigureMeasurements.FromCheckIn(checkIn, heightInches);
        var hasData = FigureMeasurements.HasAnyMeasurement(checkIn);

        _heightInches = measurements.HeightInches;
        NoDataLabel.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;

        var figure = BodyFigureBuilder.Build(measurements, CreateMaterial(hasData));

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

    /// <summary>Slightly translucent and desaturated with no data, so the reference figure reads as
    /// a placeholder without disappearing.</summary>
    private static Material CreateMaterial(bool hasData)
    {
        var body = new SolidColorBrush(hasData
            ? Color.FromRgb(0xB4, 0xC4, 0xD2)
            : Color.FromRgb(0x63, 0x74, 0x83))
        {
            Opacity = hasData ? 1.0 : 0.55
        };

        return new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(body),
                // A tight highlight keeps the surface reading as a solid form rather than flat paint.
                new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x88, 0xDE, 0xF0, 0xFF)), 32),
            }
        };
    }

    private void PositionCamera()
    {
        // Framed from the figure's own height so a tall and a short person both fill the panel.
        var target = new Point3D(0, _heightInches * 0.52, 0);
        var distance = _heightInches * 1.78;
        Camera.Position = new Point3D(0, target.Y + _heightInches * 0.05, distance);
        Camera.LookDirection = target - Camera.Position;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragOrigin = e.GetPosition(this);
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
        _yaw.Angle += (current.X - _dragOrigin.X) * 0.45;
        // Pitch is clamped: letting it pass vertical flips the figure and is disorienting.
        _pitch.Angle = Math.Clamp(_pitch.Angle + (current.Y - _dragOrigin.Y) * 0.25, -MaxPitch, MaxPitch);
        _dragOrigin = current;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }
}
