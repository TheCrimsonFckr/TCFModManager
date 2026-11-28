namespace TCFModManager.Core.Models;

//
// One published version of this app on sp-mod, with its release notes as the HTML sp-mod stores
// them in. Null notes means the version was published without any.
//
public sealed record AppRelease(string Version, DateTimeOffset? PublishedAt, string? Notes);
