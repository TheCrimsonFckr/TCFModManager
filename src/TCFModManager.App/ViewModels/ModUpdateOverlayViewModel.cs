namespace TCFModManager.App.ViewModels;

// Shared, app-lifetime signal for showing the update dialog for an installed mod, awaitable so callers know when it closes.
public sealed class ModUpdateOverlayViewModel
{
    // Set by MainWindow. Constructs and shows the update dialog for the given card, returning once it's closed.
    public Func<InstalledModCardViewModel, Task>? ShowAsync { get; set; }
}
