using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Wpf.Ui.Appearance;
using MediaColor = System.Windows.Media.Color;

namespace TCFModManager.App.ViewModels;

public enum ThemeStartKind
{
    Base,
    Preset,
    Saved,
}

public sealed class ThemeStartOption : LocalizedViewModel
{
    private readonly string? _key;
    private readonly string? _name;

    public ThemeStartOption(ThemeStartKind kind, string? key, string? name, string? id, ThemeBase themeBase)
    {
        Kind = kind;
        _key = key;
        _name = name;
        Id = id;
        Base = themeBase;
    }

    public ThemeStartKind Kind { get; }

    public string? Id { get; }

    public ThemeBase Base { get; }

    public string Label => _key is not null ? LocalizationService.Get(_key) : _name ?? "";

    public override string ToString() => Label;
}

public sealed class ThemeBaseOption(string key, ThemeBase value) : LocalizedViewModel
{
    public ThemeBase Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

public sealed class ThemeCornerOption(string? key, double? value) : LocalizedViewModel
{
    public double? Value { get; } = value;

    public string Label => key is not null
        ? LocalizationService.Get(key)
        : (Value ?? 0).ToString("0.#");

    public override string ToString() => Label;
}

public sealed class ThemeFontOption(string? family) : LocalizedViewModel
{
    public string? Family { get; } = family;

    public string Label => Family ?? Strings.ThemeEditor_FontDefault;

    public override string ToString() => Label;
}

public sealed partial class ThemeColourRow : LocalizedViewModel
{
    private readonly string _labelKey;
    private readonly Action<ThemeColourRow> _changed;
    private bool _loading;

    public ThemeColourRow(string labelKey, PropertyInfo? property, Action<ThemeColourRow> changed)
    {
        _labelKey = labelKey;
        Property = property;
        _changed = changed;
    }

    public PropertyInfo? Property { get; }

    public string Label => LocalizationService.Get(_labelKey);

    public IReadOnlyList<string> Palette => ThemeEditorViewModel.Palette;

    public ThemeColor? Value { get; private set; }

    private ThemeColor? _fallback;

    [ObservableProperty]
    private string _hex = "";

    [ObservableProperty]
    private string _placeholder = "";

    [ObservableProperty]
    private Brush _swatch = Brushes.Transparent;

    [ObservableProperty]
    private bool _isInvalid;

    [ObservableProperty]
    private bool _isPickerOpen;

    public void Load(ThemeColor? value, ThemeColor? fallback, string placeholder)
    {
        _loading = true;
        Value = value;
        _fallback = fallback;
        Hex = value?.ToString() ?? "";
        Placeholder = placeholder;
        IsInvalid = false;
        UpdateSwatch();
        _loading = false;
    }

    partial void OnHexChanged(string value)
    {
        if (_loading) return;

        if (string.IsNullOrWhiteSpace(value))
        {
            Value = null;
            IsInvalid = false;
        }
        else if (ThemeColor.TryParse(value, out var color))
        {
            Value = color;
            IsInvalid = false;
        }
        else
        {
            IsInvalid = true;
            return;
        }

        UpdateSwatch();
        _changed(this);
    }

    [RelayCommand]
    private void Reset() => Hex = "";

    [RelayCommand]
    private void Pick(string hex)
    {
        Hex = hex;
        IsPickerOpen = false;
    }

    private void UpdateSwatch()
    {
        var shown = Value ?? _fallback;
        if (shown is not { } c)
        {
            Swatch = Brushes.Transparent;
            return;
        }

        var brush = new SolidColorBrush(MediaColor.FromArgb(c.A, c.R, c.G, c.B));
        brush.Freeze();
        Swatch = brush;
    }
}

public sealed class ThemeColourGroup(string headerKey, IReadOnlyList<ThemeColourRow> rows) : LocalizedViewModel
{
    public string Header => LocalizationService.Get(headerKey);

    public IReadOnlyList<ThemeColourRow> Rows { get; } = rows;
}

public sealed partial class ThemeEditorViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) => LocalizationService.Text(format, values);

    public static readonly IReadOnlyList<string> Palette =
    [
        "#000000", "#0F1924", "#12120F", "#171D25", "#202020", "#2A2C30", "#3A3A3A", "#606060",
        "#9E9E9E", "#C7D5E0", "#D9D4C3", "#F1F2F4", "#FFFFFF", "#1A9FFF", "#0078D4", "#4CC2FF",
        "#7AA2D6", "#14B8A6", "#16A34A", "#75B022", "#8BAA55", "#B4A46B", "#E5B143", "#FFD400",
        "#D97706", "#E06A65", "#C42B1C", "#FF99A4", "#8B5CF6", "#D946EF", "#1A3045", "#2A475E",
    ];

    private static readonly PropertyInfo[] ColourProperties = typeof(ThemeColors).GetProperties();

    private readonly ThemeStore _store = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly ThemeColourRow _accentRow;
    private readonly List<ThemeColourRow> _rows = [];
    private ThemeDefinition _working = new();
    private ThemeDefinition _original = new();
    private bool _loading;

    public ThemeEditorViewModel()
    {
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            AppTheme.Preview(_working.Clone());
        };

        _accentRow = new ThemeColourRow(nameof(Strings.ThemeEditor_ColourAccent), null, RowChanged);

        Groups =
        [
            Group(nameof(Strings.ThemeEditor_SectionSurfaces),
                (nameof(Strings.ThemeEditor_ColourBackground), nameof(ThemeColors.Background)),
                (nameof(Strings.ThemeEditor_ColourPage), nameof(ThemeColors.Page)),
                (nameof(Strings.ThemeEditor_ColourSurface), nameof(ThemeColors.Surface)),
                (nameof(Strings.ThemeEditor_ColourSurfaceSecondary), nameof(ThemeColors.SurfaceSecondary)),
                (nameof(Strings.ThemeEditor_ColourFlyout), nameof(ThemeColors.Flyout)),
                (nameof(Strings.ThemeEditor_ColourStroke), nameof(ThemeColors.Stroke)),
                (nameof(Strings.ThemeEditor_ColourDivider), nameof(ThemeColors.Divider))),
            Group(nameof(Strings.ThemeEditor_SectionText),
                (nameof(Strings.ThemeEditor_ColourTextPrimary), nameof(ThemeColors.TextPrimary)),
                (nameof(Strings.ThemeEditor_ColourTextSecondary), nameof(ThemeColors.TextSecondary)),
                (nameof(Strings.ThemeEditor_ColourTextTertiary), nameof(ThemeColors.TextTertiary)),
                (nameof(Strings.ThemeEditor_ColourTextDisabled), nameof(ThemeColors.TextDisabled))),
            Group(nameof(Strings.ThemeEditor_SectionStatus),
                (nameof(Strings.ThemeEditor_ColourSuccess), nameof(ThemeColors.Success)),
                (nameof(Strings.ThemeEditor_ColourCaution), nameof(ThemeColors.Caution)),
                (nameof(Strings.ThemeEditor_ColourCritical), nameof(ThemeColors.Critical)),
                (nameof(Strings.ThemeEditor_ColourAttention), nameof(ThemeColors.Attention))),
            Group(nameof(Strings.ThemeEditor_SectionFootprint),
                (nameof(Strings.ThemeEditor_ColourFootprintCpu), nameof(ThemeColors.FootprintCpu)),
                (nameof(Strings.ThemeEditor_ColourFootprintGpu), nameof(ThemeColors.FootprintGpu)),
                (nameof(Strings.ThemeEditor_ColourFootprintMemory), nameof(ThemeColors.FootprintMemory)),
                (nameof(Strings.ThemeEditor_ColourFootprintStartup), nameof(ThemeColors.FootprintStartup)),
                (nameof(Strings.ThemeEditor_ColourFootprintDisk), nameof(ThemeColors.FootprintDisk)),
                (nameof(Strings.ThemeEditor_ColourFootprintNeutral), nameof(ThemeColors.FootprintNeutral))),
        ];

        RefreshStartOptions();
        LoadInitial();
    }

    public ThemeColourRow AccentRow => _accentRow;

    public IReadOnlyList<ThemeColourGroup> Groups { get; }

    public ObservableCollection<ThemeStartOption> StartOptions { get; } = [];

    public IReadOnlyList<ThemeBaseOption> BaseOptions { get; } =
    [
        new(nameof(Strings.Options_ThemeDark), ThemeBase.Dark),
        new(nameof(Strings.Options_ThemeLight), ThemeBase.Light),
    ];

    public ObservableCollection<ThemeCornerOption> CornerOptions { get; } =
    [
        new(nameof(Strings.ThemeEditor_CornersDefault), null),
        new(nameof(Strings.ThemeEditor_CornersSquare), 0),
        new(nameof(Strings.ThemeEditor_CornersRound), 8),
    ];

    public ObservableCollection<ThemeFontOption> FontOptions { get; } =
    [
        new(null),
        .. Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).Select(f => new ThemeFontOption(f)),
    ];

    public ObservableCollection<string> Problems { get; } = [];

    [ObservableProperty]
    private ThemeStartOption? _selectedStart;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private ThemeBaseOption? _selectedBase;

    [ObservableProperty]
    private ThemeCornerOption? _selectedCorner;

    [ObservableProperty]
    private ThemeFontOption? _selectedFont;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _canSave;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private bool _isSaved;

    private ThemeColourGroup Group(string headerKey, params (string Label, string Property)[] rows)
    {
        var made = rows
            .Select(r => new ThemeColourRow(r.Label, ColourProperties.Single(p => p.Name == r.Property), RowChanged))
            .ToList();

        _rows.AddRange(made);
        return new ThemeColourGroup(headerKey, made);
    }

    private void RefreshStartOptions()
    {
        StartOptions.Clear();
        StartOptions.Add(new ThemeStartOption(ThemeStartKind.Base, nameof(Strings.Options_ThemeDark), null, null, ThemeBase.Dark));
        StartOptions.Add(new ThemeStartOption(ThemeStartKind.Base, nameof(Strings.Options_ThemeLight), null, null, ThemeBase.Light));

        foreach (var preset in BuiltInThemes.All)
        {
            StartOptions.Add(new ThemeStartOption(ThemeStartKind.Preset, BuiltInThemes.NameKey(preset.Id), null, preset.Id, preset.Base));
        }

        foreach (var saved in _store.LoadAll())
        {
            StartOptions.Add(new ThemeStartOption(ThemeStartKind.Saved, null, saved.Name, saved.Id, saved.Base));
        }
    }

    private void LoadInitial()
    {
        var storedId = AppTheme.StoredThemeId;
        var current = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Light ? ThemeBase.Light : ThemeBase.Dark;

        var option = StartOptions.FirstOrDefault(o => o.Id is not null && string.Equals(o.Id, storedId, StringComparison.OrdinalIgnoreCase))
            ?? StartOptions.First(o => o.Kind == ThemeStartKind.Base && o.Base == current);

        _loading = true;
        SelectedStart = option;
        _loading = false;
        LoadFrom(option);
    }

    partial void OnSelectedStartChanged(ThemeStartOption? value)
    {
        if (_loading || value is null) return;

        if (IsDirty && !ConfirmDiscard())
        {
            _loading = true;
            SelectedStart = StartOptions.FirstOrDefault(o => o.Id is not null && o.Id == _working.Id) ?? SelectedStart;
            _loading = false;
            return;
        }

        LoadFrom(value);
    }

    private void LoadFrom(ThemeStartOption option)
    {
        var theme = option.Kind switch
        {
            ThemeStartKind.Saved => _store.Find(option.Id!) ?? new ThemeDefinition { Base = option.Base },
            ThemeStartKind.Preset => CopyOf(BuiltInThemes.Find(option.Id)!, option.Label),
            _ => new ThemeDefinition { Name = Strings.ThemeEditor_NewName, Base = option.Base },
        };

        _original = theme.Clone();
        Load(theme);
        Status = null;
    }

    private static ThemeDefinition CopyOf(ThemeDefinition preset, string presetName)
    {
        var copy = preset.Clone();
        copy.Id = "";
        copy.Name = Text(Strings.ThemeEditor_CopyNameFormat, presetName);
        return copy;
    }

    private void Load(ThemeDefinition theme)
    {
        _working = theme.Clone();
        _loading = true;

        Name = _working.Name;
        SelectedBase = BaseOptions.First(b => b.Value == _working.Base);
        SelectedCorner = CornerFor(_working.CornerRadius);
        SelectedFont = FontFor(_working.FontFamily);
        LoadRows();

        _loading = false;
        IsDirty = false;
        IsSaved = ThemeStore.IsCustomId(_working.Id) && _store.Find(_working.Id) is not null;
        AfterChange(preview: true);
    }

    private void LoadRows()
    {
        var fallback = ThemeMath.BaseColors(_working.Base);
        var footprint = FootprintDefaults();

        _accentRow.Load(_working.Accent, null, Strings.ThemeEditor_AccentWindows);

        foreach (var row in _rows)
        {
            var own = (ThemeColor?)row.Property!.GetValue(_working.Colors);
            var basis = (ThemeColor?)row.Property.GetValue(fallback)
                ?? (footprint.TryGetValue(row.Property.Name, out var f) ? f : null);

            row.Load(own, basis, basis?.ToString() ?? "");
        }
    }

    private static Dictionary<string, ThemeColor> FootprintDefaults()
    {
        var result = new Dictionary<string, ThemeColor>(StringComparer.Ordinal);
        var defaults = Application.Current?.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains("AppDefaults", StringComparison.OrdinalIgnoreCase) == true);
        if (defaults is null) return result;

        foreach (var name in new[] { "Cpu", "Gpu", "Memory", "Startup", "Disk", "Neutral" })
        {
            if (defaults[$"Footprint{name}Color"] is MediaColor c)
            {
                result[$"Footprint{name}"] = new ThemeColor(c.A, c.R, c.G, c.B);
            }
        }

        return result;
    }

    private ThemeCornerOption CornerFor(double? radius)
    {
        var match = CornerOptions.FirstOrDefault(o => o.Value == radius);
        if (match is not null) return match;

        var custom = new ThemeCornerOption(null, radius);
        CornerOptions.Add(custom);
        return custom;
    }

    private ThemeFontOption FontFor(string? family)
    {
        var match = FontOptions.FirstOrDefault(o => string.Equals(o.Family, family, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;

        var missing = new ThemeFontOption(family);
        FontOptions.Insert(1, missing);
        return missing;
    }

    private void RowChanged(ThemeColourRow row)
    {
        if (_loading) return;

        if (ReferenceEquals(row, _accentRow))
        {
            _working.Accent = row.Value;
            _working.AccentShades = null;
        }
        else
        {
            var copy = _working.Colors with { };
            row.Property!.SetValue(copy, row.Value);
            _working.Colors = copy;
        }

        MarkChanged();
    }

    partial void OnNameChanged(string value)
    {
        if (_loading) return;
        _working.Name = value;
        MarkChanged(preview: false);
    }

    partial void OnSelectedBaseChanged(ThemeBaseOption? value)
    {
        if (_loading || value is null) return;
        _working.Base = value.Value;

        _loading = true;
        LoadRows();
        _loading = false;

        MarkChanged();
    }

    partial void OnSelectedCornerChanged(ThemeCornerOption? value)
    {
        if (_loading || value is null) return;
        _working.CornerRadius = value.Value;
        MarkChanged();
    }

    partial void OnSelectedFontChanged(ThemeFontOption? value)
    {
        if (_loading || value is null) return;
        _working.FontFamily = value.Family;
        MarkChanged();
    }

    private void MarkChanged(bool preview = true)
    {
        IsDirty = true;
        Status = null;
        AfterChange(preview);
    }

    private void AfterChange(bool preview)
    {
        var checks = ThemeRules.Check(_working);

        Problems.Clear();
        foreach (var check in checks) Problems.Add(ThemeProblems.Describe(check));

        CanSave = !checks.Any(c => c.BlocksSave) && (IsDirty || !IsSaved);

        if (preview)
        {
            _previewTimer.Stop();
            _previewTimer.Start();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!ThemeRules.CanSave(_working)) return;

        var saved = _store.Save(_working.Clone());
        _working.Id = saved.Id;
        _original = saved.Clone();

        AppTheme.SetCustom(saved.Id);

        IsDirty = false;
        IsSaved = true;
        Status = Text(Strings.ThemeEditor_SavedFormat, saved.Name);

        RefreshStartOptions();
        _loading = true;
        SelectedStart = StartOptions.FirstOrDefault(o => o.Id == saved.Id);
        _loading = false;

        AfterChange(preview: false);
    }

    private bool CanRevert() => IsDirty;

    [RelayCommand(CanExecute = nameof(CanRevert))]
    private void Revert()
    {
        Load(_original);
        Status = null;
    }

    private bool CanDelete() => IsSaved;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        var answer = AskWindow.Show(new AskRequest
        {
            Title = Strings.ThemeEditor_DeleteTitle,
            Lead = Text(Strings.ThemeEditor_DeleteLeadFormat, _working.Name),
            First = Strings.ThemeEditor_Delete,
            Danger = AskChoice.First,
            Default = AskChoice.Cancel,
        });

        if (answer != AskChoice.First) return;

        var id = _working.Id;
        var name = _working.Name;
        _store.Delete(id);

        if (string.Equals(AppTheme.StoredThemeId, id, StringComparison.OrdinalIgnoreCase)) AppTheme.Set(AppTheme.Stored);

        RefreshStartOptions();
        LoadInitial();
        Status = Text(Strings.ThemeEditor_DeletedFormat, name);
    }

    [RelayCommand]
    private void Export()
    {
        if (!ThemeStore.IsCustomId(_working.Id)) _working.Id = Guid.NewGuid().ToString("D");

        var dialog = new SaveFileDialog
        {
            Title = Strings.ThemeEditor_ExportDialogTitle,
            Filter = ThemeFileDialog.Filter,
            FileName = SuggestedFileName(_working.Name),
            AddExtension = true,
            DefaultExt = ThemeFile.Extension,
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, ThemeFile.Write(_working));
            Status = Text(Strings.ThemeEditor_ExportedFormat, _working.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = Text(Strings.ThemeEditor_ExportFailedFormat, ex.Message);
        }
    }

    [RelayCommand]
    private void Import()
    {
        if (IsDirty && !ConfirmDiscard()) return;

        var dialog = new OpenFileDialog
        {
            Title = Strings.ThemeEditor_ImportDialogTitle,
            Filter = ThemeFileDialog.Filter,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        var import = _store.ImportFile(dialog.FileName);
        if (import.Theme is not { } theme)
        {
            Status = ThemeProblems.Describe(import.Problem ?? ThemeFileProblem.Unreadable);
            return;
        }

        RefreshStartOptions();
        var option = StartOptions.FirstOrDefault(o => o.Id == theme.Id);

        _loading = true;
        SelectedStart = option;
        _loading = false;

        _original = theme.Clone();
        Load(theme);

        Status = import.Replaced
            ? Text(Strings.ThemeEditor_ImportReplacedFormat, theme.Name)
            : Text(Strings.ThemeEditor_ImportedFormat, theme.Name);
    }

    public bool ConfirmClose()
    {
        if (!IsDirty) return true;

        var answer = AskWindow.Show(new AskRequest
        {
            Title = Strings.ThemeEditor_UnsavedTitle,
            Lead = Text(Strings.ThemeEditor_UnsavedFormat, _working.Name),
            First = Strings.Common_Save,
            Second = Strings.ThemeEditor_DontSave,
            Default = AskChoice.Cancel,
        });

        switch (answer)
        {
            case AskChoice.First when CanSave:
                Save();
                return true;
            case AskChoice.Second:
                return true;
            default:
                return false;
        }
    }

    public void OnClosed()
    {
        _previewTimer.Stop();
        AppTheme.RestoreStored();
    }

    private bool ConfirmDiscard() =>
        AskWindow.Show(new AskRequest
        {
            Title = Strings.ThemeEditor_UnsavedTitle,
            Lead = Text(Strings.ThemeEditor_DiscardChangesFormat, _working.Name),
            First = Strings.ThemeEditor_DiscardButton,
            Cancel = Strings.ThemeEditor_KeepEditing,
            Default = AskChoice.Cancel,
        }) == AskChoice.First;

    private static string SuggestedFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return (clean.Length == 0 ? "theme" : clean) + ThemeFile.Extension;
    }
}
