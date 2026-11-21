using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TCFModManager.App.Behaviors;
using TCFModManager.App.Services;

namespace TCFModManager.App.Controls;

public sealed class CardFieldsPanel : StackPanel
{
    public static readonly DependencyProperty FieldsProperty = DependencyProperty.Register(
        nameof(Fields), typeof(IReadOnlyList<CardFieldSpec>), typeof(CardFieldsPanel),
        new PropertyMetadata(null, (d, _) => ((CardFieldsPanel)d).Rebuild()));

    public static readonly DependencyProperty OneLineProperty = DependencyProperty.Register(
        nameof(OneLine), typeof(bool), typeof(CardFieldsPanel),
        new PropertyMetadata(false, (d, _) => ((CardFieldsPanel)d).Rebuild()));

    public static readonly DependencyProperty TemplatePrefixProperty = DependencyProperty.Register(
        nameof(TemplatePrefix), typeof(string), typeof(CardFieldsPanel),
        new PropertyMetadata("CardField.Installed.", (d, _) => ((CardFieldsPanel)d).Rebuild()));

    private IReadOnlyList<CardFieldSpec>? _built;
    private bool _builtOneLine;
    private string? _builtPrefix;

    public IReadOnlyList<CardFieldSpec>? Fields
    {
        get => (IReadOnlyList<CardFieldSpec>?)GetValue(FieldsProperty);
        set => SetValue(FieldsProperty, value);
    }

    public bool OneLine
    {
        get => (bool)GetValue(OneLineProperty);
        set => SetValue(OneLineProperty, value);
    }

    public string TemplatePrefix
    {
        get => (string)GetValue(TemplatePrefixProperty);
        set => SetValue(TemplatePrefixProperty, value);
    }

    private void Rebuild()
    {
        var fields = Fields;
        if (ReferenceEquals(fields, _built) && _builtOneLine == OneLine && _builtPrefix == TemplatePrefix) return;
        _built = fields;
        _builtOneLine = OneLine;
        _builtPrefix = TemplatePrefix;

        Children.Clear();
        if (fields is not { Count: > 0 }) return;

        if (OneLine) BuildOneLine(fields);
        else BuildStacked(fields);
    }

    private void BuildStacked(IReadOnlyList<CardFieldSpec> fields)
    {
        for (var i = 0; i < fields.Count;)
        {
            var kind = fields[i].Kind;
            var run = new List<CardFieldSpec>();
            while (i < fields.Count && fields[i].Kind == kind && kind is CardFieldKind.Inline or CardFieldKind.Chip)
                run.Add(fields[i++]);

            switch (kind)
            {
                case CardFieldKind.Inline:
                    Add(new InlineFieldLine { Fields = run }, CardDensity.LineMarginKey);
                    break;
                case CardFieldKind.Chip:
                    var row = new WrapPanel();
                    foreach (var chip in run) row.Children.Add(Presenter(chip));
                    Add(row, CardDensity.ChipRowMarginKey);
                    break;
                case CardFieldKind.Block:
                    Add(Presenter(fields[i++]), CardDensity.LineMarginKey);
                    break;
                default:
                    i++;
                    break;
            }
        }
    }

    private void BuildOneLine(IReadOnlyList<CardFieldSpec> fields)
    {
        var text = fields.Where(f => f.Kind != CardFieldKind.Button && f.Text is not null).ToList();
        var chips = fields.Where(f => f.Kind != CardFieldKind.Button && f.Text is null).ToList();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var line = new InlineFieldLine { Fields = text, LinksEnabled = false, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(line, 0);
        grid.Children.Add(line);

        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        foreach (var chip in chips) chipRow.Children.Add(Presenter(chip));
        Grid.SetColumn(chipRow, 1);
        grid.Children.Add(chipRow);

        Add(grid, CardDensity.LineMarginKey);
    }

    private void Add(FrameworkElement element, string marginKey)
    {
        element.SetResourceReference(MarginProperty, marginKey);
        Children.Add(element);
    }

    private ContentPresenter Presenter(CardFieldSpec field)
    {
        var presenter = new ContentPresenter();
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding());
        presenter.SetResourceReference(ContentPresenter.ContentTemplateProperty, TemplatePrefix + field.Key);
        return presenter;
    }
}
