using AaronOS.Modules.BodyMeasurements.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AaronOS.Modules.BodyMeasurements.Views;

/// <summary>
/// A front-facing body outline whose segment widths come from the most recent check-in. Drawn
/// procedurally rather than from an image so it needs no art assets and can reflect real numbers,
/// including left/right asymmetry — useful when tracking one arm or leg against the other.
///
/// It always renders. With no measurements it falls back to average proportions, drawn dimmed with
/// a "no measurements yet" label, so the figure is present as a frame of reference from day one.
///
/// Measurements are circumferences, not widths, so each is converted with width = C / pi before
/// scaling — using circumference directly as a width would draw everyone far too wide.
/// </summary>
public partial class BodySilhouette : UserControl
{
    private const double CanvasWidth = 220;
    private const double CentreX = CanvasWidth / 2;
    private const double PixelsPerInch = 9.0;

    // Vertical layout is fixed: only widths carry data, so the figure never becomes a funhouse
    // mirror when one measurement is missing.
    private const double HeadCentreY = 30, HeadRadiusX = 22, HeadRadiusY = 27;
    private const double NeckTop = 54, NeckBottom = 74;
    private const double ChestY = 100, WaistY = 178, HipsY = 258;
    private const double ArmTop = 82, ArmBottom = 252;
    private const double LegTop = 258, KneeY = 348, AnkleY = 428;

    /// <summary>Average adult proportions in inches, used when a measurement is absent.</summary>
    private static readonly BodyProportions Fallback = new(
        Neck: 15, Chest: 40, Waist: 34, Hips: 40, Bicep: 13, Thigh: 22, Calf: 15);

    private readonly record struct BodyProportions(
        double Neck, double Chest, double Waist, double Hips, double Bicep, double Thigh, double Calf);

    public BodySilhouette()
    {
        InitializeComponent();
        // Draw the fallback figure immediately so the control is never an empty box.
        Apply(null);
    }

    /// <summary>Redraws from a check-in. Pass null to show the dimmed reference figure.</summary>
    public void Apply(BodyCheckIn? checkIn)
    {
        var hasAny = checkIn is not null && HasAnyMeasurement(checkIn);
        NoDataLabel.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;

        var stroke = (Brush)(TryFindResource("ReactorGlow") ?? Brushes.DeepSkyBlue);
        var fill = new SolidColorBrush(Color.FromArgb(hasAny ? (byte)38 : (byte)16, 0x4C, 0xC2, 0xFF));
        var outline = stroke.Clone();
        outline.Opacity = hasAny ? 0.95 : 0.32;
        outline.Freeze();

        FigureCanvas.Children.Clear();
        DrawFigure(Resolve(checkIn), fill, outline);
    }

    private static bool HasAnyMeasurement(BodyCheckIn c) =>
        c.NeckIn is not null || c.ChestIn is not null || c.WaistIn is not null || c.HipsIn is not null
        || c.BicepLeftIn is not null || c.BicepRightIn is not null
        || c.ThighLeftIn is not null || c.ThighRightIn is not null
        || c.CalfLeftIn is not null || c.CalfRightIn is not null;

    /// <summary>Per-side widths in canvas units, falling back per-measurement rather than
    /// all-or-nothing, so a check-in with only a waist still draws that waist truthfully.</summary>
    private static (double Neck, double Chest, double Waist, double Hips,
        double BicepL, double BicepR, double ThighL, double ThighR, double CalfL, double CalfR)
        Resolve(BodyCheckIn? c)
    {
        double W(decimal? circumference, double fallbackInches) =>
            (double)(circumference ?? (decimal)fallbackInches) / Math.PI * PixelsPerInch;

        return (
            W(c?.NeckIn, Fallback.Neck),
            W(c?.ChestIn, Fallback.Chest),
            W(c?.WaistIn, Fallback.Waist),
            W(c?.HipsIn, Fallback.Hips),
            W(c?.BicepLeftIn, Fallback.Bicep), W(c?.BicepRightIn, Fallback.Bicep),
            W(c?.ThighLeftIn, Fallback.Thigh), W(c?.ThighRightIn, Fallback.Thigh),
            W(c?.CalfLeftIn, Fallback.Calf), W(c?.CalfRightIn, Fallback.Calf));
    }

    private void DrawFigure(
        (double Neck, double Chest, double Waist, double Hips,
         double BicepL, double BicepR, double ThighL, double ThighR, double CalfL, double CalfR) w,
        Brush fill, Brush outline)
    {
        // Shoulders sit a little wider than the chest measurement itself.
        var shoulder = w.Chest * 1.12;

        Add(new Ellipse
        {
            Width = HeadRadiusX * 2,
            Height = HeadRadiusY * 2,
            Fill = fill,
            Stroke = outline,
            StrokeThickness = 2
        }, CentreX - HeadRadiusX, HeadCentreY - HeadRadiusY);

        AddPolygon(fill, outline,
            (CentreX - w.Neck / 2, NeckTop), (CentreX + w.Neck / 2, NeckTop),
            (CentreX + w.Neck / 2, NeckBottom), (CentreX - w.Neck / 2, NeckBottom));

        // Torso: shoulders -> chest -> waist -> hips, mirrored down the centre line.
        AddPolygon(fill, outline,
            (CentreX - shoulder / 2, NeckBottom), (CentreX + shoulder / 2, NeckBottom),
            (CentreX + w.Chest / 2, ChestY), (CentreX + w.Waist / 2, WaistY),
            (CentreX + w.Hips / 2, HipsY), (CentreX - w.Hips / 2, HipsY),
            (CentreX - w.Waist / 2, WaistY), (CentreX - w.Chest / 2, ChestY));

        // Arms, each drawn at its own width so a difference between sides is visible.
        var leftArmX = CentreX - shoulder / 2 - w.BicepL / 2;
        var rightArmX = CentreX + shoulder / 2 + w.BicepR / 2;
        AddLimb(fill, outline, leftArmX, w.BicepL, ArmTop, ArmBottom);
        AddLimb(fill, outline, rightArmX, w.BicepR, ArmTop, ArmBottom);

        // Legs: thigh tapering to calf, one either side of the centre line.
        var legOffset = w.Hips / 4;
        AddTaperedLimb(fill, outline, CentreX - legOffset, w.ThighL, w.CalfL);
        AddTaperedLimb(fill, outline, CentreX + legOffset, w.ThighR, w.CalfR);
    }

    private void AddLimb(Brush fill, Brush outline, double centreX, double width, double top, double bottom) =>
        AddPolygon(fill, outline,
            (centreX - width / 2, top), (centreX + width / 2, top),
            (centreX + width / 2, bottom), (centreX - width / 2, bottom));

    private void AddTaperedLimb(Brush fill, Brush outline, double centreX, double thighWidth, double calfWidth) =>
        AddPolygon(fill, outline,
            (centreX - thighWidth / 2, LegTop), (centreX + thighWidth / 2, LegTop),
            (centreX + calfWidth / 2, KneeY), (centreX + calfWidth / 2, AnkleY),
            (centreX - calfWidth / 2, AnkleY), (centreX - calfWidth / 2, KneeY));

    private void AddPolygon(Brush fill, Brush outline, params (double X, double Y)[] points)
    {
        var polygon = new Polygon
        {
            Fill = fill,
            Stroke = outline,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            Points = new PointCollection(points.Select(p => new Point(p.X, p.Y)))
        };
        Add(polygon, 0, 0);
    }

    private void Add(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        FigureCanvas.Children.Add(element);
    }
}
