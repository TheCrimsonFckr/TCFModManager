namespace TCFModManager.Core.ServerMap;

//
// Finds the Server Map mod's config folder on THIS machine: the place the operator's published list
// and the server's shared key both live.
//
// Layouts are probed rather than mapped, the same way the server's own PayloadLoader finds its
// payload: from a starting folder, look for TCFModManager\ServerMap\config\ there and at each
// folder above it. That covers the app sitting beside the install, inside it, and the 4.0 vs 4.1
// difference in where the server half lives, without this class knowing which is which.
//
// Nothing here decides whether this machine IS a server. The folder existing is the honest test,
// and it is the one that matters: it means whoever is at this keyboard already owns those files.
//
public static class ServerMapConfigFolder
{
    private static readonly string[] Segments = ["TCFModManager", "ServerMap", "config"];

    //
    // Both starting points are tried because either can be the odd one out: the app usually lives in
    // <SPT root>\TCFModManager\, so its own folder finds it immediately, but an install managed from
    // an app copied elsewhere only resolves from the configured SPT path.
    //
    public static bool TryFind(string? sptInstallPath, out string directory, string? appDirectory = null)
    {
        foreach (var start in new[] { appDirectory ?? AppContext.BaseDirectory, sptInstallPath })
        {
            if (TryFindFrom(start, out directory)) return true;
        }

        directory = "";
        return false;
    }

    // The config folder at or above startDirectory, if there is one.
    public static bool TryFindFrom(string? startDirectory, out string directory)
    {
        directory = "";

        if (string.IsNullOrWhiteSpace(startDirectory)) return false;

        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(startDirectory!);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return false;
        }

        while (current != null)
        {
            var candidate = Path.Combine([current.FullName, .. Segments]);

            if (Directory.Exists(candidate))
            {
                directory = candidate;
                return true;
            }

            current = current.Parent;
        }

        return false;
    }
}
