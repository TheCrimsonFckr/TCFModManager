using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views.Play;

// The Profile page's experimental Play card. DataContext is the page's PlayViewModel.
public partial class PlayCard : UserControl
{
    public PlayCard()
    {
        InitializeComponent();
    }

    // The Play card's "..." - the things done now and then, kept off the card.
    private void DirectMore_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PlayViewModel vm) return;

        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender,
            Placement = PlacementMode.Bottom,
            DataContext = vm,
        };

        menu.Items.Add(new MenuItem
        {
            Header = vm.WipeNextStart ? Strings.Play_DirectWipeCancel : Strings.Play_DirectWipe,
            ToolTip = vm.IsRemotePlay ? Strings.Play_RemoteWipeToolTip : Strings.Play_DirectWipeToolTip,
            Command = vm.ToggleWipeCommand,
            IsEnabled = vm.SelectedProfile is not null && !vm.IsPlaying && (vm.WipeNextStart || !vm.SelectedProfileIsProtected),
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectNewProfile,
            ToolTip = vm.IsRemotePlay ? Strings.Play_RemoteNewProfileToolTip : Strings.Play_DirectNewProfileToolTip,
            Command = vm.NewProfileCommand,
            IsEnabled = !vm.IsPlaying,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectDeleteProfile,
            ToolTip = vm.IsRemotePlay ? Strings.Play_RemoteDeleteProfileToolTip : Strings.Play_DirectDeleteProfileToolTip,
            Command = vm.AskDeleteProfileCommand,
            IsEnabled = vm.SelectedProfile is not null && !vm.IsPlaying && !vm.IsGameRunning && !vm.SelectedProfileIsProtected,
        });

        menu.Items.Add(new Separator());

        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectClearCache,
            ToolTip = Strings.Play_DirectClearCacheToolTip,
            Command = vm.ClearGameCacheNowCommand,
            IsEnabled = !vm.IsPlaying && !vm.IsGameRunning,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectOpenLauncher,
            ToolTip = Strings.Play_DirectOpenLauncherToolTip,
            Command = vm.StartClientCommand,
            IsEnabled = vm.CanStartClient,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectSettings,
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => AppNavigation.Navigate(typeof(OptionsPage))),
        });

        menu.IsOpen = true;
    }
}
