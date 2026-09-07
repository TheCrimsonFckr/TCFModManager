namespace TCFModManager.Core.ServerMap;

//
// Finds the shared key belonging to a Server Map server running on THIS machine.
//
// The point is the operator. They generated the key by running their own server, they own the file
// it is in, and asking them to go and find it in a folder to paste it back into the app one window
// away is busywork - so the app reads it for them. Nothing is disclosed by this: anyone who can run
// this app on that machine can already open the file.
//
// It is not a substitute for being given a key. On any machine that is not the server, no file is
// found and the box stays empty, which is correct - a key belongs to one server, and reading a
// local one to send to a remote server would just be a 401 with extra steps.
//
// Layouts are probed rather than mapped, the same way the server's own PayloadLoader finds its
// payload: from a starting folder, look for TCFModManager\ServerMap\config\ at that folder and each
// one above it. That covers the app sitting beside the install, inside it, and the 4.0 vs 4.1
// difference in where the server half lives, without this class knowing which is which.
//
public static class ServerMapKeyFile
{
    public const string FileName = "servermap-key.txt";

    //
    // The key this machine's own server expects, or null when this machine is not that server.
    //
    // Both starting points are tried because either can be the odd one out: the app usually lives in
    // <SPT root>\TCFModManager\, so its own folder finds the file immediately, but an install
    // managed from an app copied elsewhere only resolves from the configured SPT path.
    //
    public static string? TryReadLocal(string? sptInstallPath, string? appDirectory = null) =>
        TryFind(sptInstallPath, out var path, appDirectory) && TryRead(path, out var key) ? key : null;

    // The key file in this machine's Server Map config folder, if there is one.
    public static bool TryFind(string? sptInstallPath, out string path, string? appDirectory = null)
    {
        path = "";

        if (!ServerMapConfigFolder.TryFind(sptInstallPath, out var directory, appDirectory)) return false;

        var candidate = Path.Combine(directory, FileName);
        if (!File.Exists(candidate)) return false;

        path = candidate;
        return true;
    }

    //
    // The key as written. Trimmed but otherwise untouched - the server compares keys with dashes,
    // spaces and case normalised away, so there is nothing to be gained by reformatting it here and
    // something to be lost: what the app shows should be exactly what the operator sees in the file
    // and pastes to a friend.
    //
    public static bool TryRead(string path, out string key)
    {
        key = "";

        try
        {
            if (!File.Exists(path)) return false;

            var contents = File.ReadAllText(path).Trim();

            if (string.IsNullOrWhiteSpace(contents)) return false;

            key = contents;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
