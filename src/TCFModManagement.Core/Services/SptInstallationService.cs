using System.Diagnostics;

namespace TCFModManager.Core.Services;

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

    public static bool TryGetInstalledVersion(string? installPath, out string? version, out string? error)
    {
        version = null;
        error = null;

        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            error = "No SPT install folder set.";
            return false;
        }

        if (!TryFindServerExe(installPath, out var exePath))
        {
            error = $"Couldn't find an SPT server executable under \"{installPath}\" - make sure this is the SPT server install folder (the one containing SPT.Server.exe).";
            return false;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var raw = (info.FileVersion ?? "").Trim();
            var parts = raw.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                error = $"\"{Path.GetFileName(exePath)}\" didn't have a recognizable file version.";
                return false;
            }

            // Normalized to major.minor.patch; any non-integer part falls back to "0".
            version = $"{Part(parts, 0)}.{Part(parts, 1)}.{Part(parts, 2)}";
            return true;
        }
        catch (Exception ex)
        {
            error = $"Couldn't read the file version from \"{Path.GetFileName(exePath)}\": {ex.Message}";
            return false;
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
