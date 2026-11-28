using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// OPEN-79: one row of the update page's version history.
public sealed partial class AppReleaseRowViewModel(AppRelease release, bool isRunning) : LocalizedViewModel
{
    public string Version => release.Version;

    public bool IsRunning { get; } = isRunning;

    public string? PublishedText => release.PublishedAt is { } published
        ? LocalizationService.Text(Strings.AppUpdate_PublishedFormat, published.ToLocalTime())
        : null;

    public bool HasNotes => release.Notes is not null;

    public bool HasNoNotes => !HasNotes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownNotes))]
    private bool _isExpanded;

    public string? ShownNotes => IsExpanded ? release.Notes : null;
}
