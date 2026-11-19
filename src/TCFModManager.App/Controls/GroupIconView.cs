using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TCFModManager.App.Services;

namespace TCFModManager.App.Controls;

// OPEN-57: one of the app's own group icons, drawn in the theme's text colour at any size.
public sealed class GroupIconView : Viewbox
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
        nameof(Key), typeof(string), typeof(GroupIconView), new PropertyMetadata(null, (d, _) => ((GroupIconView)d).Apply()));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(GroupIconView), new PropertyMetadata(null, (d, _) => ((GroupIconView)d).ApplyStroke()));

    private readonly Path _path = new()
    {
        StrokeThickness = 1.5,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        SnapsToDevicePixels = false,
    };

    public GroupIconView()
    {
        Stretch = Stretch.Uniform;
        Child = new Canvas { Width = 24, Height = 24, Children = { _path } };
        ApplyStroke();
    }

    public string? Key
    {
        get => (string?)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    private void Apply() => _path.Data = GroupIcons.GeometryFor(Key);

    private void ApplyStroke()
    {
        if (Stroke is { } brush) _path.Stroke = brush;
        else _path.SetResourceReference(Shape.StrokeProperty, "TextFillColorPrimaryBrush");
    }
}
