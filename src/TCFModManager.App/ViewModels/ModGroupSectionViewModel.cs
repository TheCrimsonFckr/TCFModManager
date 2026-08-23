using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// One collapsible section in the Mod Groups window: either a real, user-created ModGroup, or the
// fixed "Ungrouped" bucket (GroupId null) holding every installed mod nothing was assigned to.
public partial class ModGroupSectionViewModel : ObservableObject
{
    // Null for the Ungrouped bucket, which can't be renamed, deleted, reordered, or collapsed.
    public Guid? GroupId { get; init; }

    public bool IsRealGroup => GroupId is not null;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChevronGlyph))]
    private bool _isCollapsed;

    // Fluent icon name for the header's collapse toggle - matches a SymbolRegular member in the
    // WPF-UI build in use, same convention as ModStatusDisplay.Glyph.
    public string ChevronGlyph => IsCollapsed ? "ChevronRight24" : "ChevronDown24";

    // True while the header's name TextBox is shown in place of the TextBlock.
    [ObservableProperty]
    private bool _isEditing;

    public ObservableCollection<InstalledModCardViewModel> Items { get; } = [];

    public string CountLabel => Items.Count == 1 ? "1 mod" : $"{Items.Count} mods";

    public ModGroupSectionViewModel()
    {
        // CountLabel has no backing field for Items.CollectionChanged to invalidate on its own.
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CountLabel));
    }

    public static ModGroupSectionViewModel FromGroup(ModGroup group) => new()
    {
        GroupId = group.Id,
        Name = group.Name,
        IsCollapsed = group.IsCollapsed,
    };

    public static ModGroupSectionViewModel Ungrouped() => new()
    {
        GroupId = null,
        Name = "Ungrouped",
    };
}
