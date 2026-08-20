using System.Windows.Controls;
using TCFModManagement.App.ViewModels;

namespace TCFModManagement.App.Views;

public partial class OptionsPage : Page
{
    public OptionsViewModel ViewModel { get; } = new();

    public OptionsPage()
    {
        DataContext = ViewModel;
        InitializeComponent();
    }
}
