using System.Windows;
using System.Windows.Controls;

namespace TCFModManager.App.Controls;

// A StackPanel whose children slide to a new position rather than jumping (OPEN-26, see LayoutGlide).
public class GlideStackPanel : StackPanel
{
    private readonly LayoutGlide _glide;

    public GlideStackPanel()
    {
        _glide = new LayoutGlide(this);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        _glide.AfterArrange();
        return arranged;
    }
}
