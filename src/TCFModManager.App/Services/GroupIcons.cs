using System.Windows.Media;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Services;

public sealed record GroupIconDefinition(string Key, string NameKey, string PathData)
{
    public string Name => LocalizationService.Get(NameKey);
}

// OPEN-57: the app's own group icons - 24x24 outlines drawn with a 1.5 stroke, round caps and joins.
public static class GroupIcons
{
    public static IReadOnlyList<GroupIconDefinition> All { get; } =
    [
        new("rifle", nameof(Strings.GroupIcon_Rifle), "M2,10h3.5l1,1.5H14l1,-1.5h7v2.5h-5.5l-1.5,1.5h-2l-1.5,5h-2.2l1.2,-5H9.2l-.8,2.5H6.2l.8,-2.5H2zM17.5,10V8.5h2.5V10"),
        new("pistol", nameof(Strings.GroupIcon_Pistol), "M4,7h15v4h-7l-1,2h-1.5l-1.5,5H5l1.8,-7H4zM11,13h-1.5"),
        new("scope", nameof(Strings.GroupIcon_Scope), "M12,12m-7,0a7,7,0,1,0,14,0a7,7,0,1,0,-14,0M12,2v5M12,17v5M2,12h5M17,12h5M12,12h0.01"),
        new("ammo", nameof(Strings.GroupIcon_Ammo), "M7,21V11c0,-3,1,-6,2,-7c1,1,2,4,2,7v10zM13,21V11c0,-3,1,-6,2,-7c1,1,2,4,2,7v10zM7,17h4M13,17h4"),
        new("grenade", nameof(Strings.GroupIcon_Grenade), "M12,21a6,6,0,0,0,6,-6c0,-3.5,-2.7,-6,-6,-6s-6,2.5,-6,6a6,6,0,0,0,6,6zM10,9V6.5h4V9M14,7l4,-2M16,4.5l2,3M9,13h6M9,16h6"),
        new("helmet", nameof(Strings.GroupIcon_Helmet), "M3,16c0,-5,4,-9,9,-9s9,4,9,9zM2,16h20M8,16v2h8v-2M12,7v4M8,11h8"),
        new("armor", nameof(Strings.GroupIcon_Armor), "M8,3h8l1,3,3,1v13H4V7l3,-1zM9,3c0,2,1.3,3,3,3s3,-1,3,-3M7,11h10M7,15h10"),
        new("backpack", nameof(Strings.GroupIcon_Backpack), "M7,7c0,-2.5,2,-4,5,-4s5,1.5,5,4M5,9c0,-1.5,1,-2,2,-2h10c1,0,2,.5,2,2v11H5zM8,13h8v4H8zM10,7V5.5h4V7"),
        new("medkit", nameof(Strings.GroupIcon_Medkit), "M4,7h16v13H4zM9,7V4.5h6V7M12,10.5v6M9,13.5h6"),
        new("map", nameof(Strings.GroupIcon_Map), "M3,6l6,-2,6,2,6,-2v14l-6,2,-6,-2,-6,2zM9,4v14M15,6v14"),
        new("compass", nameof(Strings.GroupIcon_Compass), "M12,12m-9,0a9,9,0,1,0,18,0a9,9,0,1,0,-18,0M15.5,8.5l-2,5,-5,2,2,-5z"),
        new("skull", nameof(Strings.GroupIcon_Skull), "M12,3c-4.4,0,-7.5,3,-7.5,7,0,2.4,1,4,2.5,5v3h10v-3c1.5,-1,2.5,-2.6,2.5,-5,0,-4,-3.1,-7,-7.5,-7zM9,11.5h0.01M15,11.5h0.01M10,18v2M14,18v2M12,14l-1,1.5h2z"),
        new("bot", nameof(Strings.GroupIcon_Bot), "M6,8h12v10H6zM12,8V5M12,4.5h0.01M9.5,12h0.01M14.5,12h0.01M9.5,15h5M4,11v4M20,11v4"),
        new("trader", nameof(Strings.GroupIcon_Trader), "M5,7c0,-1.4,3.1,-2.5,7,-2.5s7,1.1,7,2.5,-3.1,2.5,-7,2.5S5,8.4,5,7zM5,7v3.5c0,1.4,3.1,2.5,7,2.5s7,-1.1,7,-2.5V7M5,10.5V14c0,1.4,3.1,2.5,7,2.5s7,-1.1,7,-2.5v-3.5M5,14v3.5c0,1.4,3.1,2.5,7,2.5s7,-1.1,7,-2.5V14"),
        new("quest", nameof(Strings.GroupIcon_Quest), "M6,3h10l3,3v15H6zM16,3v3h3M9,11l1.5,1.5L13,10M9,16l1.5,1.5L13,15M15,11.5h1.5M15,16.5h1.5"),
        new("knife", nameof(Strings.GroupIcon_Knife), "M3,21l5,-5M8,16l2,2M7,15l2.5,2.5M9.5,14.5l9,-10.5c1,2,1,5,-1,7l-6,5.5z"),
        new("key", nameof(Strings.GroupIcon_Key), "M8,15m-4,0a4,4,0,1,0,8,0a4,4,0,1,0,-8,0M11,12l9,-9M17,6l2.5,2.5M15,8l2,2"),
        new("door", nameof(Strings.GroupIcon_Door), "M6,21V4c0,-.6,.4,-1,1,-1h10c.6,0,1,.4,1,1v17M3,21h18M14.5,12h0.01M18,6l3,-1v14l-3,2"),
        new("hideout", nameof(Strings.GroupIcon_Hideout), "M3,11l9,-7,9,7M5,9.5V20h14V9.5M10,20v-5h4v5"),
        new("gear", nameof(Strings.GroupIcon_Gear), "M12.0,5.0L13.8,3.2L15.4,3.7L15.9,6.2L16.9,7.1L19.5,7.0L20.3,8.6L18.9,10.6L19.0,12.0L20.8,13.8L20.3,15.4L17.8,15.9L16.9,16.9L17.0,19.5L15.4,20.3L13.4,18.9L12.0,19.0L10.2,20.8L8.6,20.3L8.1,17.8L7.1,16.9L4.5,17.0L3.7,15.4L5.1,13.4L5.0,12.0L3.2,10.2L3.7,8.6L6.2,8.1L7.1,7.1L7.0,4.5L8.6,3.7L10.6,5.1ZM12,12m-3,0a3,3,0,1,0,6,0a3,3,0,1,0,-6,0"),
        new("wrench", nameof(Strings.GroupIcon_Wrench), "M5.46,16.42L11.30,10.58A4.5,4.5,0,0,1,17.20,4.54L15.01,6.73L17.27,8.99L19.46,6.80A4.5,4.5,0,0,1,13.42,12.70L7.58,18.54A1.5,1.5,0,0,1,5.46,16.42z"),
        new("eye", nameof(Strings.GroupIcon_Eye), "M2,12c2.5,-4.5,6,-7,10,-7s7.5,2.5,10,7c-2.5,4.5,-6,7,-10,7S4.5,16.5,2,12zM12,12m-3,0a3,3,0,1,0,6,0a3,3,0,1,0,-6,0"),
        new("audio", nameof(Strings.GroupIcon_Audio), "M4,9h4l5,-4v14l-5,-4H4zM16,9c1,.8,1.5,1.8,1.5,3s-.5,2.2,-1.5,3M18.5,6.5c1.8,1.4,2.8,3.3,2.8,5.5s-1,4.1,-2.8,5.5"),
        new("brush", nameof(Strings.GroupIcon_Brush), "M18.5,3.5l2,2,-9,9,-2,-2zM9.5,12.5c-2,0,-3.5,1.5,-3.5,3.5,0,1.5,-1,2.5,-2.5,3,1,.8,2.5,1.5,4,1.5,2.5,0,4,-2,4,-4z"),
        new("server", nameof(Strings.GroupIcon_Server), "M4,4h16v6H4zM4,14h16v6H4zM7.5,7h0.01M7.5,17h0.01M11,7h6M11,17h6"),
        new("library", nameof(Strings.GroupIcon_Library), "M4,4h4v16H4zM10,4h4v16h-4zM16.5,4.5l3.5,-1,4,15.5,-3.5,1z"),
        new("bolt", nameof(Strings.GroupIcon_Bolt), "M13,2L4,14h7l-1,8,9,-12h-7z"),
        new("shield", nameof(Strings.GroupIcon_Shield), "M12,3l8,3v6c0,5,-3.5,8,-8,9,-4.5,-1,-8,-4,-8,-9V6zM8.5,12l2.5,2.5,4.5,-5"),
        new("star", nameof(Strings.GroupIcon_Star), "M12,3l2.8,5.7,6.2,.9,-4.5,4.4,1.1,6.2L12,17.3,6.4,20.2l1.1,-6.2L3,9.6l6.2,-.9z"),
        new("flask", nameof(Strings.GroupIcon_Flask), "M9,3h6M10,3v6l-5.5,9.5A1.7,1.7,0,0,0,6,21h12a1.7,1.7,0,0,0,1.5,-2.5L14,9V3M7.5,15h9"),
        new("flag", nameof(Strings.GroupIcon_Flag), "M5,21V4M5,4h11l-2,4,2,4H5"),
    ];

    private static readonly Dictionary<string, Geometry> Geometries = new(StringComparer.OrdinalIgnoreCase);

    public static GroupIconDefinition? Find(string? key) =>
        key is null ? null : All.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));

    public static Geometry? GeometryFor(string? key)
    {
        if (Find(key) is not { } icon) return null;
        if (Geometries.TryGetValue(icon.Key, out var cached)) return cached;

        var geometry = Geometry.Parse(icon.PathData);
        geometry.Freeze();
        Geometries[icon.Key] = geometry;
        return geometry;
    }
}
