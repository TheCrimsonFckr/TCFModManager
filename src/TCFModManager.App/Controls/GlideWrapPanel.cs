using System.Windows;
using System.Windows.Controls;

namespace TCFModManager.App.Controls;

// A WrapPanel whose children slide to a new row or column rather than jumping (OPEN-26, see LayoutGlide).
public class GlideWrapPanel : WrapPanel
{
    private readonly LayoutGlide _glide;

    public GlideWrapPanel()
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
