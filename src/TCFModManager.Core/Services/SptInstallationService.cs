using System.Diagnostics;

namespace TCFModManager.Core.Services;
//
// Why the SPT version could not be read.
//
public enum SptVersionProblem
{
    // No folder is configured, or it is not there any more. Carries nothing.
    NoInstallFolder,

    // The folder holds no SPT server exe in any of the layouts this app knows. Carries InstallPath.
    NoServerExe,

    // The exe is there but its file version is not two or more numbers. Carries ExeName.
    NoVersionInExe,

    // Reading the file version threw. Carries ExeName and Error.
    CouldNotReadExe,
}

//
// The version, or why there isn't one. Version and Problem are mutually exclusive; which of the
// other values is filled depends on Problem - see the comment on each case.
//
public sealed record SptVersionReading
{
    public string? Version { get; init; }

    public SptVersionProblem? Problem { get; init; }

    public string? InstallPath { get; init; }

    public string? ExeName { get; init; }

    public Exception? Error { get; init; }

    public bool Found => Version is not null;
}


// 
// Reads the installed SPT server version from the server executable's embedded file version
// resource, rather than parsing SPT_Data/Server/configs/core.json.
// 
public static class SptInstallationService
{
    // Candidate server exe paths/names across known install layouts: both the older Aki.Server.exe
    // and newer SPT.Server.exe naming, at the install root and inside nested SPT/ or SPT_Runtime/ folders.
    private static readonly string[] ServerExeCandidates =
    [
        "SPT.Server.exe",
        Path.Combine("SPT_Runtime", "SPT.Server.exe"),
        Path.Combine("SPT", "SPT.Server.exe"),
        "Aki.Server.exe",
        Path.Combine("Aki.Server", "Aki.Server.exe"),
        Path.Combine("Server", "Server.exe"),
        Path.Combine("SPT", "Server.exe"),
    ];

    //
    // Reads the SPT version out of the server exe's file version.
    //
    // Returns what happened rather than a sentence about it - the four wordings this used to build
    // were the last user-facing English in Core on this path. SptEnvironmentViewModel words them.
    //
    public static SptVersionReading GetInstalledVersion(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return new SptVersionReading { Problem = SptVersionProblem.NoInstallFolder };

        if (!TryFindServerExe(installPath, out var exePath))
        {
            return new SptVersionReading
            {
                Problem = SptVersionProblem.NoServerExe,
                InstallPath = installPath,
            };
        }

        var exeName = Path.GetFileName(exePath);

        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var raw = (info.FileVersion ?? "").Trim();
            var parts = raw.Split('.', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                return new SptVersionReading
                {
                    Problem = SptVersionProblem.NoVersionInExe,
                    ExeName = exeName,
                };
            }

            // Normalized to major.minor.patch; any non-integer part falls back to "0".
            return new SptVersionReading { Version = $"{Part(parts, 0)}.{Part(parts, 1)}.{Part(parts, 2)}" };
        }
        catch (Exception ex)
        {
            return new SptVersionReading
            {
                Problem = SptVersionProblem.CouldNotReadExe,
                ExeName = exeName,
                Error = ex,
            };
        }

        static string Part(string[] p, int i) => i < p.Length && int.TryParse(p[i], out var n) ? n.ToString() : "0";
    }

    // Returns the folder (relative to <paramref name="installPath"/>, "" for the install
    // root itself) that holds the server exe for this install. Server-side content shipping
    // alongside the exe (e.g. user/mods) lives under this same folder.
    public static bool TryGetServerRoot(string? installPath, out string serverRoot)
    {
        serverRoot = "";

        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath)) return false;
        if (!TryFindServerExe(installPath, out var exePath)) return false;

        var exeDir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(exeDir)) return true; // serverRoot stays ""

        var relative = Path.GetRelativePath(installPath, exeDir);
        serverRoot = relative == "." ? "" : relative;
        return true;
    }

    private static bool TryFindServerExe(string installPath, out string exePath)
    {
        exePath = "";

        foreach (var relative in ServerExeCandidates)
        {
            var candidate = Path.Combine(installPath, relative);
            if (File.Exists(candidate))
            {
                exePath = candidate;
                return true;
            }
        }

        // Wildcard fallback for layouts that don't match any named candidate above.
        foreach (var dir in new[] { installPath, Path.Combine(installPath, "SPT_Runtime"), Path.Combine(installPath, "SPT") })
        {
            if (!Directory.Exists(dir)) continue;

            var hit = Directory.EnumerateFiles(dir, "*Server*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (hit is not null)
            {
                exePath = hit;
                return true;
            }
        }

        return false;
    }
}
