using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class AddonFilesWindow : FluentWindow
{
    public sealed partial class FileChoice(string path, string shown, bool chosen) : ObservableObject
    {
        public string Path { get; } = path;

        public string Shown { get; } = shown;

        [ObservableProperty]
        private bool _isChosen = chosen;
    }

    private readonly List<FileChoice> _files;

    private AddonFilesWindow(string addonName, string modName, List<FileChoice> files)
    {
        InitializeComponent();
        DialogFit.Attach(this);

        WindowTitleBar.Title = LocalizationService.Text(Strings.AddonFiles_TitleFormat, addonName);
        DescriptionText.Text = LocalizationService.Text(Strings.AddonFiles_DescriptionFormat, addonName, modName);

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        _files = files;
        FileList.ItemsSource = _files;
        UpdateCount();

        Loaded += (_, _) => FilterBox.Focus();
    }

    //
    // The files of a mod's folders to pick an addon's from: path is what is stored (install-relative),
    // shown is what the list reads. Null on Cancel; an empty list unlinks the addon.
    //
    public static IReadOnlyList<string>? Ask(string addonName, string modName, IEnumerable<(string Path, string Shown, bool Chosen)> files)
    {
        var window = new AddonFilesWindow(addonName, modName, [.. files.Select(f => new FileChoice(f.Path, f.Shown, f.Chosen))]);
        return window.ShowDialog() == true ? [.. window._files.Where(f => f.IsChosen).Select(f => f.Path)] : null;
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = FilterBox.Text.Trim();
        FileList.ItemsSource = filter.Length == 0
            ? _files
            : _files.Where(f => f.Shown.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void File_Toggled(object sender, RoutedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        var count = _files.Count(f => f.IsChosen);
        CountText.Text = Strings.AddonFiles_Chosen(count, count);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
