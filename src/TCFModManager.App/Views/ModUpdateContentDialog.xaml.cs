using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class ModUpdateContentDialog : ContentDialog
{
    // Ensures WPF-UI's ContentDialog style applies to this subclass.
    static ModUpdateContentDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ModUpdateContentDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog)));
    }

    public ModUpdateDialogViewModel ViewModel { get; }

    // Uses WPF-UI's legacy ContentPresenter-based ContentDialog constructor.
#pragma warning disable CS0618 // Type or member is obsolete
    public ModUpdateContentDialog(ContentPresenter host, InstalledModCardViewModel mod) : base(host)
    {
        ViewModel = new ModUpdateDialogViewModel(mod);
        InitializeComponent();
        DataContext = ViewModel;
        Title = mod.DisplayTitle;

        // Fire-and-forget load; the XAML's loading state covers it until it completes.
        _ = ViewModel.LoadAsync();
    }
#pragma warning restore CS0618

    // Forces a fixed dialog size after the base ContentDialog auto-sizing pass.
    protected override Size MeasureOverride(Size availableSize)
    {
        Size result = base.MeasureOverride(availableSize);

        SetCurrentValue(DialogWidthProperty, 700.0);
        SetCurrentValue(DialogHeightProperty, 660.0);

        return result;
    }
}
