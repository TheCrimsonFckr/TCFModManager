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

        // One size for every mod (Chris, 2026-10-06): sized for a full description (OPEN-12 F18) -
        // tables and pictures - so the window no longer jumps between sizes from one mod to the next.
        SetCurrentValue(DialogWidthProperty, 760.0);
        SetCurrentValue(DialogHeightProperty, 640.0);

        return result;
    }

    private void Author_Click(object sender, RoutedEventArgs e)
    {
        if (_mod.Owner is not { } owner) return;

        Hide();
        AppNavigation.ShowAuthor(owner.Id, owner.Name);
    }

    private void ViewModPageButton_Click(object sender, RoutedEventArgs e)
    {
        var url = _mod.DetailUrl;
        if (string.IsNullOrWhiteSpace(url)) return;

        // UseShellExecute opens the URL in the default browser instead of as an executable.
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
