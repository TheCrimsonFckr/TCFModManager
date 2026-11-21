using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public sealed partial class CardFieldRow : ObservableObject
{
    private readonly Action _changed;

    public CardFieldRow(CardFieldSpec spec, bool isShown, Action changed)
    {
        Spec = spec;
        _isShown = isShown;
        _changed = changed;
    }

    public CardFieldSpec Spec { get; }

    public string Label => Spec.Label;

    [ObservableProperty]
    private bool _isShown;

    [ObservableProperty]
    private bool _isEditable = true;

    partial void OnIsShownChanged(bool value) => _changed();
}

public sealed record CardOption<T>(T Value, string Label);

public sealed partial class CardLayoutEditorViewModel : LocalizedViewModel
{
    private bool _loading;

    public CardLayoutEditorViewModel(CardLayoutPage page, Guid? current, object? sample)
    {
        Page = page;
        Sample = sample;

        SizeOptions =
        [
            new(CardSize.Compact, Strings.CardLayout_SizeCompact),
            new(CardSize.Normal, Strings.CardLayout_SizeNormal),
            new(CardSize.Roomy, Strings.CardLayout_SizeRoomy),
        ];
        WidthOptions =
        [
            new(CardWidth.Narrow, Strings.CardLayout_WidthNarrow),
            new(CardWidth.Normal, Strings.CardLayout_WidthNormal),
            new(CardWidth.Wide, Strings.CardLayout_WidthWide),
        ];
        PictureOptions =
        [
            new(CardPicture.Off, Strings.CardLayout_PictureOff),
            new(CardPicture.Small, Strings.CardLayout_PictureSmall),
            new(CardPicture.Banner, Strings.CardLayout_PictureBanner),
        ];

        _preview = CardLayoutView.Default(page);
        ReloadLayouts(current);
    }

    public CardLayoutPage Page { get; }

    public bool IsInstalled => Page == CardLayoutPage.Installed;

    public string WindowTitle => IsInstalled ? Strings.CardLayout_EditorTitleInstalled : Strings.CardLayout_EditorTitleBrowse;

    public object? Sample { get; }

    public bool HasSample => Sample is not null;

    public bool ShowsListPreview => IsInstalled && HasSample;

    public ObservableCollection<CardLayoutView> Layouts { get; } = [];

    public ObservableCollection<CardFieldRow> FieldRows { get; } = [];

    public ObservableCollection<CardFieldRow> ButtonRows { get; } = [];

    public ObservableCollection<CardFieldRow> ListRows { get; } = [];

    public IReadOnlyList<CardOption<CardSize>> SizeOptions { get; }

    public IReadOnlyList<CardOption<CardWidth>> WidthOptions { get; }

    public IReadOnlyList<CardOption<CardPicture>> PictureOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private CardLayoutView? _selectedLayout;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private CardOption<CardSize>? _selectedSize;

    [ObservableProperty]
    private CardOption<CardWidth>? _selectedWidth;

    [ObservableProperty]
    private CardOption<CardPicture>? _selectedPicture;

    [ObservableProperty]
    private bool _detailsInWindow;

    [ObservableProperty]
    private CardLayoutView _preview;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsEditable => SelectedLayout is { IsDefault: false };

    public bool IsLocked => !IsEditable;

    public double PreviewWidth => 300 * Preview.WidthFactor;

    partial void OnPreviewChanged(CardLayoutView value) => OnPropertyChanged(nameof(PreviewWidth));

    partial void OnSelectedLayoutChanged(CardLayoutView? value)
    {
        if (_loading || value is null) return;
        LoadRows(value);
    }

    partial void OnSelectedSizeChanged(CardOption<CardSize>? value) => Commit();

    partial void OnSelectedWidthChanged(CardOption<CardWidth>? value) => Commit();

    partial void OnSelectedPictureChanged(CardOption<CardPicture>? value) => Commit();

    partial void OnDetailsInWindowChanged(bool value) => Commit();

    private void ReloadLayouts(Guid? select)
    {
        _loading = true;
        try
        {
            Layouts.Clear();
            foreach (var layout in AppServices.CardLayouts.ChoicesFor(Page)) Layouts.Add(layout);
            SelectedLayout = Layouts.FirstOrDefault(l => l.Id == select) ?? Layouts[0];
        }
        finally
        {
            _loading = false;
        }

        LoadRows(SelectedLayout!);
    }

    private void LoadRows(CardLayoutView view)
    {
        _loading = true;
        try
        {
            var layout = view.Layout;
            var catalog = CardLayoutFields.For(Page);

            Fill(FieldRows, catalog.Where(f => f.Kind != CardFieldKind.Button), layout.Fields, view.IsDefault);
            Fill(ButtonRows, catalog.Where(f => f.Kind == CardFieldKind.Button), layout.Fields, view.IsDefault);
            if (IsInstalled) Fill(ListRows, catalog, layout.ListFields ?? layout.Fields, view.IsDefault);

            Name = view.Name;
            SelectedSize = SizeOptions.First(o => o.Value == layout.Size);
            SelectedWidth = WidthOptions.First(o => o.Value == layout.Width);
            SelectedPicture = PictureOptions.First(o => o.Value == layout.Picture);
            DetailsInWindow = layout.DetailsInWindow;
            Preview = view;
            StatusMessage = view.IsDefault ? Strings.CardLayout_DefaultLocked : null;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Fill(ObservableCollection<CardFieldRow> rows, IEnumerable<CardFieldSpec> catalog, IReadOnlyList<string> shown, bool locked)
    {
        var specs = catalog.ToList();
        var order = shown.Where(k => specs.Any(s => s.Key == k))
            .Select(k => specs.First(s => s.Key == k))
            .Concat(specs.Where(s => !shown.Contains(s.Key)));

        rows.Clear();
        foreach (var spec in order) rows.Add(new CardFieldRow(spec, shown.Contains(spec.Key), Commit) { IsEditable = !locked });
    }

    public void MoveRow(ObservableCollection<CardFieldRow> rows, CardFieldRow row, int to)
    {
        if (!IsEditable) return;
        var from = rows.IndexOf(row);
        to = Math.Clamp(to, 0, rows.Count - 1);
        if (from < 0 || from == to) return;
        rows.Move(from, to);
        Commit();
    }

    [RelayCommand]
    private void MoveUp(CardFieldRow? row) => MoveIn(row, -1);

    [RelayCommand]
    private void MoveDown(CardFieldRow? row) => MoveIn(row, 1);

    private void MoveIn(CardFieldRow? row, int step)
    {
        if (row is null) return;
        var rows = FieldRows.Contains(row) ? FieldRows : ListRows.Contains(row) ? ListRows : null;
        if (rows is null) return;
        MoveRow(rows, row, rows.IndexOf(row) + step);
    }

    private CardLayout Current()
    {
        var source = SelectedLayout!.Layout;
        var fields = FieldRows.Where(r => r.IsShown).Select(r => r.Spec.Key)
            .Concat(ButtonRows.Where(r => r.IsShown).Select(r => r.Spec.Key))
            .ToList();

        return new CardLayout
        {
            Id = source.Id,
            Name = source.Name,
            Page = Page,
            Fields = fields,
            ListFields = IsInstalled ? [.. ListRows.Where(r => r.IsShown).Select(r => r.Spec.Key)] : null,
            Size = SelectedSize?.Value ?? CardSize.Normal,
            Width = SelectedWidth?.Value ?? CardWidth.Normal,
            Picture = SelectedPicture?.Value ?? CardPicture.Off,
            DetailsInWindow = IsInstalled && DetailsInWindow,
        };
    }

    private void Commit()
    {
        if (_loading || SelectedLayout is not { IsDefault: false }) return;

        var layout = Current();
        AppServices.CardLayouts.Save(layout);
        var saved = AppServices.CardLayouts.Find(Page, layout.Id) ?? layout;

        var view = new CardLayoutView(saved, isDefault: false);
        _loading = true;
        try
        {
            var at = Layouts.IndexOf(SelectedLayout);
            if (at >= 0) Layouts[at] = view;
            SelectedLayout = view;
        }
        finally
        {
            _loading = false;
        }

        Preview = view;
    }

    public bool CommitName()
    {
        if (SelectedLayout is not { IsDefault: false } selected) return true;
        if (string.Equals(FilterPresetRules.CleanName(Name), selected.Name, StringComparison.Ordinal)) return true;

        switch (CardLayoutRules.CheckName(AppServices.CardLayouts.All, Page, Name, selected.Layout.Id))
        {
            case PresetNameProblem.Empty:
                StatusMessage = Strings.CardLayout_NameEmpty;
                Name = selected.Name;
                return false;
            case PresetNameProblem.Taken:
                StatusMessage = Strings.CardLayout_NameTaken;
                Name = selected.Name;
                return false;
        }

        var layout = Current();
        layout.Name = FilterPresetRules.CleanName(Name);
        AppServices.CardLayouts.Save(layout);
        ReloadLayouts(layout.Id);
        StatusMessage = null;
        return true;
    }

    [RelayCommand]
    private void New()
    {
        var layout = CardLayoutFields.DefaultLayout(Page);
        layout.Id = Guid.NewGuid();
        layout.Name = CardLayoutRules.UniqueName(AppServices.CardLayouts.All, Page, Strings.CardLayout_NewName);
        AppServices.CardLayouts.Save(layout);
        ReloadLayouts(layout.Id);
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedLayout is not { } selected) return;
        var baseName = selected.IsDefault ? Strings.CardLayout_NewName : selected.Name;
        var source = selected.IsDefault ? selected.Layout : Current();
        var copy = CardLayoutRules.Copy(source, CardLayoutRules.UniqueName(AppServices.CardLayouts.All, Page, baseName));
        AppServices.CardLayouts.Save(copy);
        ReloadLayouts(copy.Id);
    }

    private bool CanDelete() => IsEditable;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedLayout is not { IsDefault: false } selected) return;

        if (AskWindow.Show(new AskRequest
            {
                Title = Strings.CardLayout_DeleteTitle,
                Lead = LocalizationService.Text(Strings.CardLayout_DeleteLeadFormat, selected.Name),
                First = Strings.CardLayout_Delete,
                Danger = AskChoice.First,
            }) != AskChoice.First)
        {
            return;
        }

        AppServices.CardLayouts.Delete(selected.Layout.Id);
        ReloadLayouts(null);
    }
}
