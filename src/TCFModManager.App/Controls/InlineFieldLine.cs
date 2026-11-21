using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using TCFModManager.App.Behaviors;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;

namespace TCFModManager.App.Controls;

public sealed class InlineFieldLine : TextBlock
{
    public static readonly DependencyProperty FieldsProperty = DependencyProperty.Register(
        nameof(Fields), typeof(IReadOnlyList<CardFieldSpec>), typeof(InlineFieldLine),
        new PropertyMetadata(null, (d, _) => ((InlineFieldLine)d).Rebuild()));

    public static readonly DependencyProperty LinksEnabledProperty = DependencyProperty.Register(
        nameof(LinksEnabled), typeof(bool), typeof(InlineFieldLine),
        new PropertyMetadata(true, (d, _) => ((InlineFieldLine)d).Rebuild()));

    private INotifyPropertyChanged? _watched;

    public InlineFieldLine()
    {
        TextTrimming = TextTrimming.CharacterEllipsis;
        SetResourceReference(FontSizeProperty, CardDensity.LineFontSizeKey);
        SetResourceReference(ForegroundProperty, "TextFillColorTertiaryBrush");

        DataContextChanged += (_, _) => { Watch(DataContext as INotifyPropertyChanged); Rebuild(); };
        Loaded += (_, _) => { Watch(DataContext as INotifyPropertyChanged); Rebuild(); };
        Unloaded += (_, _) => Watch(null);
    }

    public IReadOnlyList<CardFieldSpec>? Fields
    {
        get => (IReadOnlyList<CardFieldSpec>?)GetValue(FieldsProperty);
        set => SetValue(FieldsProperty, value);
    }

    public bool LinksEnabled
    {
        get => (bool)GetValue(LinksEnabledProperty);
        set => SetValue(LinksEnabledProperty, value);
    }

    private void Watch(INotifyPropertyChanged? item)
    {
        if (ReferenceEquals(item, _watched)) return;
        if (_watched is not null) _watched.PropertyChanged -= OnItemChanged;
        _watched = item;
        if (_watched is not null) _watched.PropertyChanged += OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) Rebuild();
        else Dispatcher.BeginInvoke(Rebuild);
    }

    private void Rebuild()
    {
        Inlines.Clear();
        var item = DataContext;
        if (Fields is not { Count: > 0 } fields || item is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        var parts = new List<string>();
        foreach (var field in fields)
        {
            var text = field.TextFor(item);
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (parts.Count > 0) Inlines.Add(new Run(" • "));
            parts.Add(text);

            if (field.IsAuthorLink && LinksEnabled)
            {
                var link = new Hyperlink(new Run(text)) { CommandParameter = item, ToolTip = Strings.Author_LinkToolTip };
                if (TryFindResource("AuthorLink") is Style style) link.Style = style;
                link.SetBinding(Hyperlink.CommandProperty, new Binding("DataContext.OpenAuthorCommand")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Page), 1),
                });
                Inlines.Add(link);
            }
            else
            {
                Inlines.Add(new Run(text));
            }
        }

        Visibility = parts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
