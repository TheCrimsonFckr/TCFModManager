using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TCFModManager.App.Localization;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace TCFModManager.App.Controls;

//
// An Options switch laid out the Windows Settings way (OPEN-47 L2): On or Off written to the left of
// a switch with no text of its own, so the switch sits at the row's right edge and the setting's
// title stays on the left.
//
public sealed class SettingSwitch : UserControl
{
    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
        nameof(IsChecked),
        typeof(bool),
        typeof(SettingSwitch),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsCheckedChanged));

    private readonly TextBlock _on;
    private readonly TextBlock _off;

    public SettingSwitch()
    {
        _on = State(nameof(Strings.Common_On));
        _off = State(nameof(Strings.Common_Off));

        var toggle = new ToggleSwitch { VerticalAlignment = VerticalAlignment.Center };
        toggle.SetBinding(ToggleSwitch.IsCheckedProperty, new Binding(nameof(IsChecked))
        {
            Source = this,
            Mode = BindingMode.TwoWay,
        });

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(_on);
        panel.Children.Add(_off);
        panel.Children.Add(toggle);

        Content = panel;
        Focusable = false;
        IsTabStop = false;
        ToolTipService.SetShowDuration(this, 20000);

        ShowState();
    }

    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    private static TextBlock State(string key)
    {
        var text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        text.SetBinding(TextBlock.TextProperty, new Binding($"[{key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
        });
        return text;
    }

    private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SettingSwitch)d).ShowState();

    private void ShowState()
    {
        _on.Visibility = IsChecked ? Visibility.Visible : Visibility.Collapsed;
        _off.Visibility = IsChecked ? Visibility.Collapsed : Visibility.Visible;
    }
}
