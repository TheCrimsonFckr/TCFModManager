namespace TCFModManager.Core.Models;

//
// An sp-mod listing the user ignored (OPEN-30). Kept in settings.json.
// Matched on the listing id; the name is only for the log.
//
public sealed class IgnoredMod
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public DateTimeOffset IgnoredAt { get; set; }
}
