namespace TCFModManager.App.ViewModels;

public sealed class DetailsCardButtons
{
    public required bool Folders { get; init; }

    public required bool Pin { get; init; }

    public required bool Remove { get; init; }

    public required Action<string?> OpenFolder { get; init; }

    public required Action<InstalledModCardViewModel> TogglePin { get; init; }

    public bool RemoveRequested { get; set; }
}
