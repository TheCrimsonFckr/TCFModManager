using System.Windows;
using System.Windows.Input;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class ReportProblemWindow : FluentWindow
{
    private ReportProblemWindow(ProblemContext context)
    {
        DataContext = new ReportProblemViewModel(context);
        InitializeComponent();
        DialogFit.Attach(this);

        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        Owner = owner is { IsVisible: true } && !ReferenceEquals(owner, this) ? owner : null;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        if (Owner is null)
        {
            Topmost = true;
            ShowInTaskbar = true;
        }

        PreviewKeyDown += OnPreviewKeyDown;
    }

    public static void Open(ProblemContext context) => new ReportProblemWindow(context).ShowDialog();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        e.Handled = true;
        Close();
    }
}
