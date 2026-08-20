using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using TCFModManager.Core.Models;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class ModDetailsContentDialog : ContentDialog
{
    // Ensures WPF-UI's ContentDialog style applies to this subclass.
    static ModDetailsContentDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ModDetailsContentDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog)));
    }

    private readonly Mod _mod;

    // Uses WPF-UI's legacy ContentPresenter-based ContentDialog constructor.
#pragma warning disable CS0618 // Type or member is obsolete
    public ModDetailsContentDialog(ContentPresenter host, Mod mod) : base(host)
    {
        _mod = mod;
        InitializeComponent();
        DataContext = mod;
        Title = mod.Name;
    }
#pragma warning restore CS0618

    // Forces a fixed dialog size after the base ContentDialog auto-sizing pass.
    protected override Size MeasureOverride(Size availableSize)
    {
        Size result = base.MeasureOverride(availableSize);

        SetCurrentValue(DialogWidthProperty, 520.0);
        SetCurrentValue(DialogHeightProperty, 478.0);

        return result;
    }

    private void ViewModPageButton_Click(object sender, RoutedEventArgs e)
    {
        var url = _mod.DetailUrl;
        if (string.IsNullOrWhiteSpace(url)) return;

        // UseShellExecute opens the URL in the default browser instead of as an executable.
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
