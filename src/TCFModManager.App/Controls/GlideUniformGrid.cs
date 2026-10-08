using System.Windows;
using System.Windows.Controls.Primitives;

namespace TCFModManager.App.Controls;

// A UniformGrid whose children slide to a new row or column rather than jumping (OPEN-26, see LayoutGlide).
public class GlideUniformGrid : UniformGrid
{
    private readonly LayoutGlide _glide;

    public GlideUniformGrid()
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
