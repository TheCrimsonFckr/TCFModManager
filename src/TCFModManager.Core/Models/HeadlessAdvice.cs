namespace TCFModManager.Core.Models;

public enum HeadlessVerdict
{
    None,
    Mentions,
    Needs,
    Avoid,
}

public sealed record HeadlessAdvice(HeadlessVerdict Verdict, IReadOnlyList<string> Quotes)
{
    public static readonly HeadlessAdvice Nothing = new(HeadlessVerdict.None, []);
}

public enum HeadlessSubjectKind
{
    Mod,
    Addon,
}

public sealed record HeadlessSubject(HeadlessSubjectKind Kind, int Id, DateTimeOffset? UpdatedAt = null)
{
    public string Key => $"{(Kind == HeadlessSubjectKind.Mod ? "mod" : "addon")}:{Id}";

    public static HeadlessSubject ForMod(int id, DateTimeOffset? updatedAt = null) => new(HeadlessSubjectKind.Mod, id, updatedAt);

    public static HeadlessSubject ForAddon(int id, DateTimeOffset? updatedAt = null) => new(HeadlessSubjectKind.Addon, id, updatedAt);
}

public sealed record HeadlessAdviceForEntry(HeadlessAdvice Advice, bool FromParent);
