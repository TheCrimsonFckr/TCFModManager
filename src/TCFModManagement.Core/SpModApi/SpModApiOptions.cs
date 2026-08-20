namespace TCFModManager.Core.SpModApi;

public sealed class SpModApiOptions
{
    public string BaseUrl { get; init; } = "https://sp-mod.com";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    // Sent as the User-Agent header.
    public string UserAgent { get; init; } = "TCFModManager/0.1";
}
