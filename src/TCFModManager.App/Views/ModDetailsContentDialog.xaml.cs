using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;
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

    // The mod's own addons, filled in after the dialog opens - the cached addon catalog answers
    // this without a network call once it has loaded for the session.
    private readonly AddonsSectionViewModel _addons = new();

    // Uses WPF-UI's legacy ContentPresenter-based ContentDialog constructor.
#pragma warning disable CS0618 // Type or member is obsolete
    public ModDetailsContentDialog(ContentPresenter host, ModDetailsRequest request) : base(host)
    {
        _mod = request.Mod;
        InitializeComponent();
        DataContext = _mod;
        AddonsHost.DataContext = _addons;
        Title = _mod.Name;

        // Fire-and-forget: the section is collapsed until it has something to show, so the dialog
        // opens at once whether or not this mod has addons.
        _ = _addons.LoadAsync(_mod.Id, _mod.Name, request.InstalledVersion);
    }
#pragma warning restore CS0618

    // Forces a fixed dialog size after the base ContentDialog auto-sizing pass. The taller size is
    // for a mod that has addons, whose section would otherwise open already scrolled.
    protected override Size MeasureOverride(Size availableSize)
    {
        Size result = base.MeasureOverride(availableSize);

        SetCurrentValue(DialogWidthProperty, 520.0);
        SetCurrentValue(DialogHeightProperty, _addons.HasAddons ? 640.0 : 478.0);

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
