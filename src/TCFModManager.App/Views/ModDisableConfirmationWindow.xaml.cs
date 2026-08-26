using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

// What the user chose in ModDisableConfirmationWindow.
public enum ModDisableChoice
{
    // Back out - nothing moves.
    Cancel,

    // Move only the mods the user picked, leaving the affected ones as they are.
    ProceedOnly,

    // Move the affected mods along with them.
    ProceedWithCascade,
}

// One affected mod in the list: what it is, and why this change reaches it.
public sealed class ModDisableImpactRow(string name, string detail, bool isSoft)
{
    public string Name { get; } = name;
    public string Detail { get; } = detail;
    public bool IsSoft { get; } = isSoft;

    // A soft dependency still loads without the thing it names, so it reads as a caution rather
    // than a break.
    public string Glyph => IsSoft ? "QuestionCircle24" : "ErrorCircle24";

    public Brush Brush => IsSoft
        ? Converters.ThemeBrush.Resolve(Converters.ThemeBrush.Caution, Brushes.Goldenrod)
        : Converters.ThemeBrush.Resolve(Converters.ThemeBrush.Critical, Brushes.OrangeRed);
}

//
// Modal warning shown before a disable that would break other mods, or an enable whose own
// dependencies are still disabled. Offers to carry the affected mods along rather than only
// reporting them.
//
public partial class ModDisableConfirmationWindow : FluentWindow
{
    private ModDisableConfirmationWindow(bool disabling, IReadOnlyList<string> targets, IReadOnlyList<ModDisableImpactRow> impact)
    {
        InitializeComponent();

        var verb = disabling ? "Disable" : "Enable";
        var targetLabel = targets.Count == 1 ? targets[0] : $"{targets.Count} mods";

        WindowTitleBar.Title = Title = $"{verb} {targetLabel}?";

        SummaryText.Text = disabling
            ? $"{impact.Count} other mod(s) depend on what you're about to disable."
            : $"{impact.Count} mod(s) this needs are still disabled.";

        TargetsText.Text = targets.Count == 1
            ? $"Selected: {targets[0]}"
            : $"Selected: {string.Join(", ", targets)}";

        ImpactHeading.Text = disabling ? "Affected mods" : "Still disabled";

        ImpactList.ItemsSource = impact;

        ProceedOnlyButton.Content = $"{verb} anyway";
        CascadeButton.Content = disabling
            ? $"{verb} these too"
            : "Enable these too";

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
    }

    //
    // Shows the warning and returns what to do. <paramref name="targets"/> are the mods the user
    // picked; <paramref name="impact"/> is everything else this reaches.
    //
    public static ModDisableChoice Confirm(bool disabling, IReadOnlyList<string> targets, IReadOnlyList<ModDisableImpactRow> impact)
    {
        var window = new ModDisableConfirmationWindow(disabling, targets, impact);
        window.ShowDialog();
        return window.Choice;
    }

    private ModDisableChoice Choice { get; set; } = ModDisableChoice.Cancel;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close(ModDisableChoice.Cancel);

    private void ProceedOnlyButton_Click(object sender, RoutedEventArgs e) => Close(ModDisableChoice.ProceedOnly);

    private void CascadeButton_Click(object sender, RoutedEventArgs e) => Close(ModDisableChoice.ProceedWithCascade);

    private void Close(ModDisableChoice choice)
    {
        Choice = choice;
        DialogResult = choice != ModDisableChoice.Cancel;
    }
}
