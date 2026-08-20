using TCFModManagement.Core.Models;

namespace TCFModManagement.App.ViewModels;

// Shared, app-lifetime signal for showing mod details. MainWindow subscribes to Requested and displays the details dialog.
public sealed class ModDetailsOverlayViewModel
{
    public event EventHandler<Mod>? Requested;

    public void Show(Mod mod) => Requested?.Invoke(this, mod);
}
