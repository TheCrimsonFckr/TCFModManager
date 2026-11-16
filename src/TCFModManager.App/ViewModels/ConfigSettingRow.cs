using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public sealed partial class ConfigSettingRow : ConfigFormItem
{
    private readonly ConfigFormViewModel _form;

    private string _colorText;

    public ConfigSettingRow(ConfigFormViewModel form, ConfigOpenFile file, BepInExConfigSetting setting, string? context = null, string? modTitle = null)
    {
        _form = form;
        File = file;
        Setting = setting;
        Context = context;
        ModTitle = modTitle;
        Kind = setting.Kind;
        _colorText = setting.Value;

        if (Kind == BepInExValueKind.Flags)
        {
            Flags = setting.AcceptableValues
                .Where(v => !string.Equals(v, "None", StringComparison.OrdinalIgnoreCase))
                .Select(v => new ConfigFlagOption(this, v))
                .ToList();
        }

        HasSlider = (Kind is BepInExValueKind.Number or BepInExValueKind.Integer)
            && setting.HasRange
            && BepInExConfigValues.TryGetRange(setting, out _, out _);
    }

    public ConfigOpenFile File { get; }

    public BepInExConfigSetting Setting { get; }

    public string? Context { get; }

    public bool HasContext => Context is not null;

    public string? ModTitle { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string? _note;

    public bool HasNote => Note is not null;

    public BepInExValueKind Kind { get; }

    public string Name => Setting.Key;

    public string Description => Setting.Description;

    public bool HasDescription => Setting.Description.Length > 0;

    public bool DiffersFromDefault => Setting.DiffersFromDefault;

    public bool CanReset => Setting.HasDefault && DiffersFromDefault;

    public string ResetToolTip => LocalizationService.Text(Strings.Configs_ResetSettingToolTipFormat, Shown(Setting.DefaultValue));

    public bool IsOn
    {
        get => BepInExConfigValues.TryReadBoolean(Setting.Value, out var on) && on;
        set => Write(BepInExConfigValues.FormatBoolean(value));
    }

    public bool HasSlider { get; }

    public double Minimum => BepInExConfigValues.TryGetRange(Setting, out var minimum, out _) ? (double)minimum : double.MinValue;

    public double Maximum => BepInExConfigValues.TryGetRange(Setting, out _, out var maximum) ? (double)maximum : double.MaxValue;

    public double Step
    {
        get
        {
            if (Kind == BepInExValueKind.Integer) return 1;
            if (!BepInExConfigValues.TryGetRange(Setting, out var minimum, out var maximum) || maximum <= minimum) return 0.01;

            return Math.Pow(10, Math.Floor(Math.Log10((double)(maximum - minimum))) - 2);
        }
    }

    public int DecimalPlaces => Kind == BepInExValueKind.Integer ? 0 : 15;

    public double? NumberValue
    {
        get => BepInExConfigValues.TryReadNumber(Setting.Value, out var number) ? (double)number : null;
        set
        {
            if (value is not { } number || !double.IsFinite(number) || Math.Abs(number) > 7.9e27) return;
            WriteNumber((decimal)number);
        }
    }

    public double SliderValue
    {
        get => NumberValue ?? Minimum;
        set
        {
            if (!double.IsFinite(value)) return;

            var step = Step;
            var snapped = Math.Round(value / step) * step;
            WriteNumber(decimal.Round((decimal)snapped, Math.Max(0, (int)-Math.Floor(Math.Log10(step)))));
        }
    }

    public IReadOnlyList<string> Choices => Setting.AcceptableValues;

    public string? SelectedChoice
    {
        get => BepInExConfigValues.MatchChoice(Setting, Setting.Value);
        set
        {
            if (value is not null) Write(value);
        }
    }

    public IReadOnlyList<ConfigFlagOption> Flags { get; } = [];

    public string ColorText
    {
        get => _colorText;
        set
        {
            if (!SetProperty(ref _colorText, value)) return;

            OnPropertyChanged(nameof(IsColorValid));
            if (BepInExConfigValues.TryReadColor(value, out var color))
            {
                Write(BepInExConfigValues.FormatColor(color), refreshColorText: false);
                OnPropertyChanged(nameof(Swatch));
            }
        }
    }

    public bool IsColorValid => BepInExConfigValues.TryReadColor(_colorText, out _);

    public Brush Swatch
    {
        get
        {
            var text = BepInExConfigValues.TryReadColor(_colorText, out _) ? _colorText : Setting.Value;
            if (!BepInExConfigValues.TryReadColor(text, out var c)) return Brushes.Transparent;

            var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            brush.Freeze();
            return brush;
        }
    }

    public string ShortcutValue
    {
        get => Setting.Value;
        set => Write(value);
    }

    public string TextValue
    {
        get => Setting.Value;
        set => Write(value);
    }

    [RelayCommand]
    private void Reset()
    {
        if (!Setting.HasDefault) return;

        Setting.ResetToDefault();
        Refresh();
        _form.Edited(this);
    }

    internal static string Shown(string? value) => string.IsNullOrEmpty(value) ? Strings.Configs_EmptyValue : value;

    internal bool Matches(string term) =>
        Setting.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Setting.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Context?.Contains(term, StringComparison.OrdinalIgnoreCase) == true
        || ModTitle?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

    internal void Refresh()
    {
        _colorText = Setting.Value;
        foreach (var flag in Flags) flag.Refresh();
        OnPropertyChanged(string.Empty);
    }

    internal bool HasFlag(string name) =>
        BepInExConfigValues.ReadFlags(Setting, Setting.Value).Contains(name, StringComparer.OrdinalIgnoreCase);

    internal void SetFlag(string name, bool on)
    {
        var picked = BepInExConfigValues.ReadFlags(Setting, Setting.Value).ToList();
        picked.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        if (on) picked.Add(name);

        Write(BepInExConfigValues.FormatFlags(Setting, picked));
    }

    private void WriteNumber(decimal number) =>
        Write(BepInExConfigValues.FormatNumber(Setting, BepInExConfigValues.Clamp(Setting, number)));

    private void Write(string value, bool refreshColorText = true)
    {
        if (string.Equals(Setting.Value, value, StringComparison.Ordinal)) return;

        Setting.Value = value;
        if (refreshColorText) _colorText = value;
        _form.Edited(this);

        OnPropertyChanged(nameof(DiffersFromDefault));
        OnPropertyChanged(nameof(CanReset));

        switch (Kind)
        {
            case BepInExValueKind.Number or BepInExValueKind.Integer:
                OnPropertyChanged(nameof(NumberValue));
                OnPropertyChanged(nameof(SliderValue));
                break;
            case BepInExValueKind.Flags:
                foreach (var flag in Flags) flag.Refresh();
                break;
        }
    }
}

public sealed class ConfigFlagOption(ConfigSettingRow row, string name) : ObservableObject
{
    public string Name { get; } = name;

    public bool IsChecked
    {
        get => row.HasFlag(Name);
        set => row.SetFlag(Name, value);
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsChecked));
}
