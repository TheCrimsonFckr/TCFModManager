using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class AddonDetailsContentDialog : Wpf.Ui.Controls.ContentDialog
{
    private readonly AddonDetailsRequest _request;

    // Uses WPF-UI's legacy ContentPresenter-based ContentDialog constructor.
#pragma warning disable CS0618 // Type or member is obsolete
    public AddonDetailsContentDialog(ContentPresenter host, AddonDetailsRequest request) : base(host)
    {
        _request = request;
        InitializeComponent();

        var parent = request.Parent;
        var record = AppServices.InstallManifest.Load().Find(request.Addon.Id, isAddon: true);

        Title = request.Addon.Name ?? LocalizationService.Text(Strings.Addon_NameFormat, request.Addon.Id);
        AddonForText.Text = LocalizationService.Text(Strings.Browse_AddonForFormat, parent.Name);
        OpenModButton.Content = LocalizationService.Text(Strings.AddonDetails_OpenModFormat, parent.Name);
        Row.DataContext = new AddonRowViewModel(request.Addon, parent.Name, request.ParentInstalledVersion, record, parent.Versions);
    }
#pragma warning restore CS0618

    protected override Size MeasureOverride(Size availableSize)
    {
        var result = base.MeasureOverride(availableSize);
        SetCurrentValue(DialogWidthProperty, 600.0);
        return result;
    }

    // The mod's full details, in place of this window.
    private async void OpenMod_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        await AppServices.Browse.LoadDetailsAsync(_request.Parent);
    }
}
