using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TCFModManager.App.Behaviors;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class InstalledPage
{
    private InstalledModCardViewModel? _windowCardPressed;

    private void HookCardLayout()
    {
        ApplyCardLayout();
        ViewModel.PropertyChanged += OnCardLayoutChanged;
    }

    private void OnCardLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstalledViewModel.CardLayout)) ApplyCardLayout();
    }

    private void ApplyCardLayout()
    {
        var layout = ViewModel.CardLayout;
        CardDensity.Apply(RootGrid.Resources, layout.Size);
        ModUpdateDialogViewModel.OpenInstallDetails = layout.DetailsInWindow;

        var key = layout.DetailsInWindow ? "InstalledWindowCard" : "InstalledExpanderCard";
        if (FindResource(key) is DataTemplate template && !ReferenceEquals(ResultsItems.ItemTemplate, template))
            ResultsItems.ItemTemplate = template;
    }

    private void WindowCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _windowCardPressed = null;
        if (sender is not FrameworkElement { DataContext: InstalledModCardViewModel mod }) return;
        if (IsInsideButton(e.OriginalSource) || IsInsideLink(e.OriginalSource)) return;

        if (IsPickingClick())
        {
            ViewModel.ClickSelect(mod, range: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
            return;
        }

        _windowCardPressed = mod;
    }

    private void WindowCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pressed = _windowCardPressed;
        _windowCardPressed = null;
        if (pressed is null || sender is not FrameworkElement { DataContext: InstalledModCardViewModel mod } || !ReferenceEquals(pressed, mod)) return;
        if (IsInsideButton(e.OriginalSource) || IsInsideLink(e.OriginalSource)) return;

        if (ViewModel.ShowDetailsCommand.CanExecute(mod)) ViewModel.ShowDetailsCommand.Execute(mod);
        e.Handled = true;
    }

    private static bool IsInsideLink(object? originalSource)
    {
        for (var node = originalSource as DependencyObject; node is not null;)
        {
            if (node is Hyperlink) return true;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }
}
