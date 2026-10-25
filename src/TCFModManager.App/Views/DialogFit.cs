using System.Windows;
using System.Windows.Threading;

namespace TCFModManager.App.Views;

internal static class DialogFit
{
    public static void Attach(Window window)
    {
        var mode = window.SizeToContent;
        if (mode == SizeToContent.Manual) return;

        window.Loaded += (_, _) =>
        {
            var before = window.ActualHeight;

            window.SizeToContent = SizeToContent.Manual;
            window.SizeToContent = mode;

            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                var shrink = before - window.ActualHeight;
                if (Math.Abs(shrink) >= 1) window.Top += shrink / 2;
            });
        };
    }
}
