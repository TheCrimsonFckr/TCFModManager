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

    public int DisabledCount => Items.Count(i => i.IsDisabled);

    // Shown after CountLabel in the header, so a group's state reads without expanding it.
    // Empty when nothing in the group is disabled, so an untouched group stays uncluttered.
    public string StateLabel => DisabledCount switch
    {
        0 => string.Empty,
        var n when n == Items.Count => "• all disabled",
        var n => $"• {n} disabled",
    };

    // Whether the header's enable-all/disable-all/invert buttons have anything to act on.
    public bool HasItems => Items.Count > 0;

    public ModGroupSectionViewModel()
    {
        // These have no backing field for Items.CollectionChanged to invalidate on their own.
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CountLabel));
            OnPropertyChanged(nameof(DisabledCount));
            OnPropertyChanged(nameof(StateLabel));
            OnPropertyChanged(nameof(HasItems));
        };
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
