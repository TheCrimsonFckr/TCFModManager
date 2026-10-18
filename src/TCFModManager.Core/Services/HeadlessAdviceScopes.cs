using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public static class HeadlessAdviceScopes
{
    public static ModListEntryScope? Suggest(HeadlessVerdict verdict, ModListEntryScope scope)
    {
        var headless = scope.HasFlag(ModListEntryScope.Headless);

        return verdict switch
        {
            HeadlessVerdict.Avoid when headless && scope != ModListEntryScope.Headless => scope & ~ModListEntryScope.Headless,
            HeadlessVerdict.Needs when !headless && scope.HasFlag(ModListEntryScope.Client) => scope | ModListEntryScope.Headless,
            _ => null,
        };
    }

    public static bool IsWarning(HeadlessVerdict verdict, ModListEntryScope scope) =>
        verdict == HeadlessVerdict.Needs && Suggest(verdict, scope) is not null;

    public static bool KeepsOff(HeadlessVerdict verdict, ModListEntryScope scope) =>
        verdict == HeadlessVerdict.Avoid && scope.HasFlag(ModListEntryScope.Headless);

    public static bool NeedsAttention(HeadlessVerdict verdict, ModListEntryScope scope) =>
        KeepsOff(verdict, scope) || IsWarning(verdict, scope);

    public static HeadlessAdviceForEntry? ForAddon(HeadlessAdvice? own, HeadlessAdvice? parent)
    {
        if (own is null) return null;
        if (own.Verdict != HeadlessVerdict.None) return new(own, FromParent: false);
        return parent is { Verdict: not HeadlessVerdict.None } ? new(parent, FromParent: true) : new(own, FromParent: false);
    }
}
