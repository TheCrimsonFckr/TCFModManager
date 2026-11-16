using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Controls;

public sealed class ConfigValueTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Boolean { get; set; }

    public DataTemplate? Number { get; set; }

    public DataTemplate? Slider { get; set; }

    public DataTemplate? Choice { get; set; }

    public DataTemplate? Flags { get; set; }

    public DataTemplate? Color { get; set; }

    public DataTemplate? Shortcut { get; set; }

    public DataTemplate? Text { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is not ConfigSettingRow row
        ? null
        : row.Kind switch
        {
            BepInExValueKind.Boolean => Boolean,
            BepInExValueKind.Number or BepInExValueKind.Integer => row.HasSlider ? Slider : Number,
            BepInExValueKind.Choice => Choice,
            BepInExValueKind.Flags => Flags,
            BepInExValueKind.Color => Color,
            BepInExValueKind.Shortcut => Shortcut,
            _ => Text,
        };
}
