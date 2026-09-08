using TCFModManager.Core.Services;

namespace TCFModManager.Core.ServerMap;

//
// Finds the Server Map mod's config folder on THIS machine: the place the operator's published list
// and the server's shared key both live.
//
// Layouts are probed rather than mapped, the same way the server's own PayloadLoader finds its
// payload: from a starting folder, look for the folder there and at each folder above it. That
// covers the app sitting beside the install, inside it, and the 4.0 vs 4.1 difference in where the
// server half lives, without this class knowing which is which.
//
// Nothing here decides whether this machine IS a server. The folder existing is the honest test,
// and it is the one that matters: it means whoever is at this keyboard already owns those files.
//
public static class ServerMapConfigFolder
{
    //
    // TCFModManager\Data\ServerMap\ - under Data\, with everything else the app keeps.
    //
    // It used to be TCFModManager\ServerMap\config\, beside the payload, on the reasoning that a
    // payload folder is what an update replaces and the operator's list must not be collateral.
    // That was right about the payload and wrong about the scope: what actually gets replaced is
    // the whole TCFModManager\ folder, and ServerMap\ sat inside it with nothing marking it as
    // something to keep. Data\ is the folder that is already understood - by the app's own updater,
    // which lays a new build over the old one specifically so Data\ and Staging\ survive, and by
    // anyone deploying by hand, who knows not to throw it away.
    //
    private static readonly string[] Segments = ["TCFModManager", "Data", "ServerMap"];

    // Where it lived before. Still read, so an install that has not been migrated yet keeps working
    // and the server does not mint a second key beside the one already handed out.
    private static readonly string[] LegacySegments = ["TCFModManager", "ServerMap", "config"];

    //
    // Both starting points are tried because either can be the odd one out: the app usually lives in
    // <SPT root>\TCFModManager\, so its own folder finds it immediately, but an install managed from
    // an app copied elsewhere only resolves from the configured SPT path.
    //
    // The current location wins over the legacy one at every level, rather than the nearest of
    // either winning: a half-migrated install has both, and the answer has to be the same one the
    // app writes to.
    //
    public static bool TryFind(string? sptInstallPath, out string directory, string? appDirectory = null)
    {
        var starts = new[] { appDirectory ?? AppContext.BaseDirectory, sptInstallPath };

        foreach (var start in starts)
        {
            if (TryFindFrom(start, Segments, out directory)) return true;
        }

        foreach (var start in starts)
        {
            if (TryFindFrom(start, LegacySegments, out directory)) return true;
        }

        directory = "";
        return false;
    }

    // The config folder at or above startDirectory, if there is one.
    public static bool TryFindFrom(string? startDirectory, out string directory) =>
        TryFindFrom(startDirectory, Segments, out directory)
        || TryFindFrom(startDirectory, LegacySegments, out directory);

    //
    // The folder as it should be from here on, whether or not it exists yet.
    //
    // Used by the migration below and by anything that has to WRITE: probing answers "where are the
    // files", which is not the same question once the answer is "nowhere yet".
    //
    public static string Preferred(string parentOfTcfModManager) =>
        Path.Combine([parentOfTcfModManager, .. Segments]);

    //
    // TEMPORARY, ADDED IN v1.12.0 - DELETE WHEN THE APP LEAVES BETA, along with LegacySegments and
    // the fallbacks above.
    //
    // Moves an existing install's key and published list out of TCFModManager\ServerMap\config\ and
    // into TCFModManager\Data\ServerMap\. Both halves read either location, so nothing breaks
    // without this - but leaving the files behind means the next hand-deploy that replaces
    // TCFModManager\ takes the key with it, which is exactly the thing the move is for.
    //
    // Runs at startup on the machine that owns the files. A no-op on every launch after the first,
    // and on any install that never ran the server mod.
    //
    public static void MigrateLegacyFolder(string? sptInstallPath, string? appDirectory = null)
    {
        foreach (var start in new[] { appDirectory ?? AppContext.BaseDirectory, sptInstallPath })
        {
            if (!TryFindFrom(start, LegacySegments, out var legacy)) continue;

            // Its own parent is <...>\ServerMap; one more up is the TCFModManager folder's parent.
            var root = Directory.GetParent(legacy)?.Parent?.Parent?.FullName;
            if (root is null) continue;

            Move(legacy, Preferred(root));
            return;
        }
    }

    //
    // Files, not the folder. The legacy folder sits beside the payload and may hold things this app
    // has never heard of; moving the tree wholesale would take those with it, and deleting it would
    // be worse. Only the two files the app knows the meaning of are carried across.
    //
    private static void Move(string legacy, string destination)
    {
        try
        {
            var files = Directory.EnumerateFiles(legacy, "*.tcfmodlist")
                .Concat(Directory.EnumerateFiles(legacy, "servermap-key.txt"))
                .ToList();

            if (files.Count == 0) return;

            Directory.CreateDirectory(destination);

            foreach (var file in files)
            {
                var target = Path.Combine(destination, Path.GetFileName(file));

                // Never over the top of one already there. Two of these means somebody has been
                // moving files by hand, and the one in the new place is the one both halves read.
                if (File.Exists(target)) continue;

                File.Move(file, target);
                AppLog.Info("ServerMap", $"moved {Path.GetFileName(file)} into {destination}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("ServerMap", $"could not move the Server Map config out of {legacy}: {ex.Message}");
        }
    }

    private static bool TryFindFrom(string? startDirectory, string[] segments, out string directory)
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
            var candidate = Path.Combine([current.FullName, .. segments]);

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
