using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;
using Button = Wpf.Ui.Controls.Button;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using TextBlock = System.Windows.Controls.TextBlock;

namespace TCFModManager.App.Controls;

public sealed class ShortcutBox : UserControl
{
    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(
        nameof(Shortcut),
        typeof(string),
        typeof(ShortcutBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnShortcutChanged));

    private static readonly Key[] ModifierKeys =
        [Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt];

    private readonly Button _capture;
    private readonly TextBlock _keys;
    private readonly TextBlock _none;
    private readonly TextBlock _press;
    private readonly TextBlock _refused;

    private bool _capturing;
    private bool _wasRefused;
    private Key? _lastModifier;

    public ShortcutBox()
    {
        _keys = Line(null);
        _none = Line(nameof(Strings.Configs_NoKey));
        _press = Line(nameof(Strings.Configs_PressAKey));
        _refused = Line(nameof(Strings.Configs_KeyNotUsable));
        _none.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");

        var lines = new Grid();
        lines.Children.Add(_keys);
        lines.Children.Add(_none);
        lines.Children.Add(_press);
        lines.Children.Add(_refused);

        _capture = new Button
        {
            MinWidth = 170,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = lines,
        };
        _capture.SetBinding(ToolTipProperty, Localized(nameof(Strings.Configs_ShortcutToolTip)));
        _capture.Click += (_, _) => StartCapture();
        _capture.PreviewKeyDown += OnKeyDown;
        _capture.PreviewKeyUp += OnKeyUp;
        _capture.PreviewMouseDown += OnMouseDown;
        _capture.LostKeyboardFocus += (_, _) => StopCapture();

        var clear = new Button
        {
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss16 },
        };
        clear.SetBinding(ToolTipProperty, Localized(nameof(Strings.Configs_ClearKeyToolTip)));
        clear.Click += (_, _) =>
        {
            StopCapture();
            Shortcut = string.Empty;
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(_capture);
        panel.Children.Add(clear);

        Content = panel;
        Focusable = false;
        IsTabStop = false;

        ShowState();
    }

    public string Shortcut
    {
        get => (string)GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    private static TextBlock Line(string? key)
    {
        var line = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        line.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        if (key is not null) line.SetBinding(TextBlock.TextProperty, Localized(key));
        return line;
    }

    private static Binding Localized(string key) => new($"[{key}]")
    {
        Source = LocalizationService.Instance,
        Mode = BindingMode.OneWay,
    };

    private static void OnShortcutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ShortcutBox)d).ShowState();

    private void StartCapture()
    {
        _capturing = true;
        _wasRefused = false;
        _lastModifier = null;
        _capture.Focus();
        ShowState();
    }

    private void StopCapture()
    {
        if (!_capturing) return;

        _capturing = false;
        _wasRefused = false;
        _lastModifier = null;
        ShowState();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;

        e.Handled = true;
        if (e.IsRepeat) return;

        var key = RealKey(e);

        if (key == Key.Escape && !HeldModifiers().Any())
        {
            StopCapture();
            return;
        }

        if (ModifierKeys.Contains(key))
        {
            _lastModifier = key;
            return;
        }

        _lastModifier = null;

        if (!UnityKeyCodes.TryFromWpfKey(key.ToString(), out var main))
        {
            Refuse();
            return;
        }

        Commit(main, HeldModifiers());
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;

        e.Handled = true;

        var key = RealKey(e);
        if (_lastModifier != key || !UnityKeyCodes.TryFromWpfKey(key.ToString(), out var main)) return;

        Commit(main, HeldModifiers().Where(m => m != main));
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_capturing || e.ChangedButton is MouseButton.Left or MouseButton.Right) return;
        if (!UnityKeyCodes.TryFromWpfMouseButton(e.ChangedButton.ToString(), out var main)) return;

        e.Handled = true;
        Commit(main, HeldModifiers());
    }

    private static Key RealKey(KeyEventArgs e) => e.Key switch
    {
        Key.System => e.SystemKey,
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.DeadCharProcessed => e.DeadCharProcessedKey,
        _ => e.Key,
    };

    private static IEnumerable<string> HeldModifiers()
    {
        foreach (var key in ModifierKeys)
        {
            if (Keyboard.IsKeyDown(key) && UnityKeyCodes.TryFromWpfKey(key.ToString(), out var name)) yield return name;
        }
    }

    private void Refuse()
    {
        _wasRefused = true;
        ShowState();
    }

    private void Commit(string main, IEnumerable<string> modifiers)
    {
        Shortcut = BepInExShortcut.Create(main, modifiers).Format();
        StopCapture();
    }

    private void ShowState()
    {
        var shown = Display(Shortcut);

        _keys.Text = shown;
        _keys.Visibility = !_capturing && shown.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _none.Visibility = !_capturing && shown.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _press.Visibility = _capturing && !_wasRefused ? Visibility.Visible : Visibility.Collapsed;
        _refused.Visibility = _capturing && _wasRefused ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Display(string? value)
    {
        if (!BepInExShortcut.TryParse(value, out var shortcut)) return value ?? string.Empty;

        return shortcut.IsEmpty ? string.Empty : string.Join(" + ", shortcut.Modifiers.Append(shortcut.MainKey));
    }
}
