using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Controls;

public sealed class SettingSlider : UserControl
{
    private readonly Slider _slider;

    private ConfigSettingRow? _row;

    private bool _updating;

    public SettingSlider()
    {
        _slider = new Slider
        {
            Width = 180,
            VerticalAlignment = VerticalAlignment.Center,
            IsSnapToTickEnabled = true,
        };
        _slider.ValueChanged += OnSliderChanged;

        Content = _slider;
        Focusable = false;
        IsTabStop = false;

        DataContextChanged += (_, _) => Attach(DataContext as ConfigSettingRow);
        Loaded += (_, _) => Attach(DataContext as ConfigSettingRow);
        Unloaded += (_, _) => Attach(null);
    }

    private void Attach(ConfigSettingRow? row)
    {
        if (_row is not null) _row.PropertyChanged -= OnRowChanged;

        _row = row;
        if (row is null) return;

        _updating = true;
        _slider.Minimum = row.Minimum;
        _slider.Maximum = row.Maximum;
        _slider.TickFrequency = row.Step;
        _slider.SmallChange = row.Step;
        _slider.LargeChange = row.Step;
        _slider.Value = row.SliderValue;
        _updating = false;

        row.PropertyChanged += OnRowChanged;
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_row is null || e.PropertyName is not (null or "" or nameof(ConfigSettingRow.SliderValue))) return;

        _updating = true;
        _slider.Value = _row.SliderValue;
        _updating = false;
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || _row is null) return;
        if (!_slider.IsMouseCaptureWithin && !_slider.IsKeyboardFocusWithin) return;

        _row.SliderValue = e.NewValue;
    }
}
