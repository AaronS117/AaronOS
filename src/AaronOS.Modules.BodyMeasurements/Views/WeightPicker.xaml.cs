using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.BodyMeasurements.Views;

/// <summary>
/// A two-column scroll picker for weight, in the spirit of the iOS Health weight wheel: scroll the
/// whole-pound column and the tenths column, and the value under the centre band is the reading.
///
/// Split into two columns rather than one long list of every tenth because 80.0–400.0 in 0.1 steps
/// is over three thousand rows to spin through; whole pounds plus tenths keeps each column short
/// enough to reach any value in a flick.
/// </summary>
public partial class WeightPicker : UserControl
{
    private const int MinWholePounds = 60;
    private const int MaxWholePounds = 500;
    private const double DefaultWeight = 170.0;

    private bool _suppressChange;

    public static readonly DependencyProperty WeightProperty = DependencyProperty.Register(
        nameof(Weight),
        typeof(double),
        typeof(WeightPicker),
        new FrameworkPropertyMetadata(
            170.0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnWeightChanged));

    /// <summary>Selected weight in pounds. Two-way bindable.</summary>
    public double Weight
    {
        get => (double)GetValue(WeightProperty);
        set => SetValue(WeightProperty, value);
    }

    public WeightPicker()
    {
        InitializeComponent();

        WholeList.ItemsSource = Enumerable.Range(MinWholePounds, MaxWholePounds - MinWholePounds + 1).ToList();
        TenthList.ItemsSource = Enumerable.Range(0, 10).ToList();

        Loaded += (_, _) =>
        {
            // With a wheel there is always a value under the band, so an "unset" weight must be
            // resolved to a real number and written back — otherwise the reading on screen and the
            // value that would be saved disagree, and saving would silently store nothing.
            if (double.IsNaN(Weight) || Weight <= 0)
            {
                Weight = DefaultWeight;
            }

            SyncColumnsFromWeight(Weight);
        };
    }

    private static void OnWeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WeightPicker picker && !picker._suppressChange)
        {
            picker.SyncColumnsFromWeight((double)e.NewValue);
        }
    }

    private void SyncColumnsFromWeight(double weight)
    {
        if (double.IsNaN(weight) || weight <= 0)
        {
            weight = DefaultWeight;
        }

        var whole = Math.Clamp((int)Math.Floor(weight), MinWholePounds, MaxWholePounds);
        var tenth = (int)Math.Round((weight - Math.Floor(weight)) * 10);
        if (tenth > 9)
        {
            tenth = 0;
            whole = Math.Min(whole + 1, MaxWholePounds);
        }

        _suppressChange = true;
        WholeList.SelectedItem = whole;
        TenthList.SelectedItem = tenth;
        _suppressChange = false;

        WholeList.ScrollIntoView(whole);
        TenthList.ScrollIntoView(tenth);
    }

    private void Column_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChange || WholeList.SelectedItem is not int whole || TenthList.SelectedItem is not int tenth)
        {
            return;
        }

        _suppressChange = true;
        Weight = whole + tenth / 10.0;
        _suppressChange = false;

        // Keep the chosen row parked under the centre band after a click as well as a scroll.
        if (ReferenceEquals(sender, WholeList))
        {
            WholeList.ScrollIntoView(whole);
        }
        else
        {
            TenthList.ScrollIntoView(tenth);
        }
    }
}
