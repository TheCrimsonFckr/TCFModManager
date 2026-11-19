using TCFModManager.App.Localization;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

public sealed class AnimationOptionItem(string key, AnimationPreference value) : LocalizedViewModel
{
    public AnimationPreference Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}
