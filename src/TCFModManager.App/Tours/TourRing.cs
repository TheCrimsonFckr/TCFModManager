using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace TCFModManager.App.Tours;

internal sealed class TourRing : Adorner
{
    private const double Gap = 3;
    private const double Thickness = 2;

    public TourRing(UIElement target) : base(target)
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var color = Application.Current?.TryFindResource("SystemAccentColor") is Color accent ? accent : Colors.DodgerBlue;
        var pen = new Pen(new SolidColorBrush(color), Thickness);
        pen.Freeze();

        var radius = Application.Current?.TryFindResource("ControlCornerRadius") is CornerRadius corner ? corner.TopLeft + Gap : 4 + Gap;
        var bounds = new Rect(AdornedElement.RenderSize);
        bounds.Inflate(Gap, Gap);
        drawingContext.DrawRoundedRectangle(null, pen, bounds, radius, radius);
    }
}
