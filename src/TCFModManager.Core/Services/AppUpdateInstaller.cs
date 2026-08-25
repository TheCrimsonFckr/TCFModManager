using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;

using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// A problem the user can act on - out of disk, a read-only app folder, a release that isn't
// packaged the way this app's own releases are. Distinct from network/API failures, which surface
// as the exception types the rest of the app already handles.
public sealed class AppUpdateException(string message, Exception? inner = null)
    : Exception(message, inner);

//
// Downloads a published release of this app from sp-mod.com and swaps it in over the running copy.
//
// A running exe can't overwrite itself, so the replacement is done from outside the process:
//
//   1. PrepareAsync downloads the release zip - the very same file the mod page's Download button
//      serves - and extracts it into ".tcfmm-update\payload" next to the exe, checking that what
//      came out actually looks like a TCF Mod Manager build before going any further.
//   2. LaunchApplyScript writes a small PowerShell script into that folder and starts it, then the
//      app shuts itself down normally.
//   3. The script waits for this process to exit, copies the new files over the app folder, and
//      starts the app again.
//
// The copy is deliberately additive (robocopy /E, never /MIR): Data\ and Staging\ sit in the same
// folder as the exe, and a mirror copy would delete the user's saved SPT path, install history,
// kept mod configs and config backups along with the old build.
//
public sealed class AppUpdateInstaller(ModDownloadService downloads)
{
    private const string ExeName = "TCFModManager.exe";
    private const string ScriptName = "apply-update.ps1";
    private const string LogName = "apply-update.log";
    private const string PayloadFolderName = "payload";

    // Hidden, and a sibling of Data\/Staging\ rather than a temp folder, so the swap never has to
    // cross a volume boundary and the whole app folder stays self-contained.
    public static string UpdateDirectory { get; } = Path.Combine(AppContext.BaseDirectory, ".tcfmm-update");

    private static string AppDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    private static string PayloadDirectory => Path.Combine(UpdateDirectory, PayloadFolderName);

    //
    // Called once at startup. Reports the outcome of a previous update attempt into the app's own
    // log - the apply script runs after this process is gone, so its log is the only record of what
    // happened - and clears out the large leftovers.
    //
    public static void SweepAfterStartup()
    {
        try
        {
            if (!Directory.Exists(UpdateDirectory)) return;

            var scriptLog = Path.Combine(UpdateDirectory, LogName);
            if (File.Exists(scriptLog))
            {
                foreach (var line in File.ReadAllLines(scriptLog))
                    if (!string.IsNullOrWhiteSpace(line))
                        AppLog.Info("AppUpdate", $"previous update: {line.Trim()}");

                File.Delete(scriptLog);
            }

            if (Directory.Exists(PayloadDirectory)) Directory.Delete(PayloadDirectory, recursive: true);
            foreach (var zip in Directory.EnumerateFiles(UpdateDirectory, "*.zip")) File.Delete(zip);

            // The script itself is usually still locked by the PowerShell process that relaunched
            // this app, so this only succeeds from the launch after that - which is fine, there's
            // nothing left in here worth the noise of retrying.
            if (Directory.EnumerateFileSystemEntries(UpdateDirectory).Any()) return;
            Directory.Delete(UpdateDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("AppUpdate", $"couldn't fully sweep {UpdateDirectory}: {ex.Message}");
        }
    }

    //
    // Downloads and unpacks <paramref name="update"/>, leaving a verified new build in
    // PayloadDirectory. Progress runs 0.0-1.0 across the whole operation, with the download taking
    // the bulk of it since that's what the user is actually waiting on.
    //
    public async Task PrepareAsync(
        AppUpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
            throw new AppUpdateException(
                $"sp-mod.com lists {update.LatestVersion} but has no file attached to it. Open the mod page and download it manually.");

        EnsureAppFolderIsWritable();
        EnsureEnoughFreeSpace(update.DownloadSizeBytes);

        // Anything left from an abandoned attempt, so a half-extracted payload can never be mistaken
        // for a good one.
        ClearWorkingFiles();
        Directory.CreateDirectory(UpdateDirectory);

        var zipPath = Path.Combine(UpdateDirectory, $"TCF-ModManager-{Sanitize(update.LatestVersion)}.zip");
        var extractDirectory = Path.Combine(UpdateDirectory, "extracted");

        AppLog.Info("AppUpdate", $"downloading {update.LatestVersion} from {update.DownloadUrl}");
        var downloadProgress = new Progress<double>(p => progress?.Report(p * 0.85));
        await downloads.DownloadAsync(update.DownloadUrl!, zipPath, downloadProgress, ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.85);

        AppLog.Info("AppUpdate", $"extracting {new FileInfo(zipPath).Length / (1024 * 1024)}MB to {extractDirectory}");
        try
        {
            // A plain zip (the release is packaged with Compress-Archive), so the framework's own
            // extractor is both the fastest option and the one that already refuses entry paths
            // pointing outside the destination folder.
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractDirectory), ct).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            throw new AppUpdateException(
                "The downloaded file isn't a readable zip - the download may have been interrupted. Try again.", ex);
        }

        progress?.Report(0.95);

        var payloadSource = FindPayloadRoot(extractDirectory)
            ?? throw new AppUpdateException(
                $"The {update.LatestVersion} release doesn't contain {ExeName} where this app expects it. "
                + "Download it from the mod page and copy it over by hand instead.");

        Directory.Move(payloadSource, PayloadDirectory);
        if (Directory.Exists(extractDirectory)) Directory.Delete(extractDirectory, recursive: true);
        File.Delete(zipPath);

        VerifyPayload();
        progress?.Report(1.0);
        AppLog.Info("AppUpdate", $"{update.LatestVersion} staged at {PayloadDirectory}");
    }

    //
    // Writes the apply script and starts it. The caller must shut the app down straight after -
    // the script is already waiting on this process to exit and will give up if it doesn't.
    //
    public static void LaunchApplyScript()
    {
        VerifyPayload();

        var scriptPath = Path.Combine(UpdateDirectory, ScriptName);
        File.WriteAllText(scriptPath, ApplyScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,

            // Never the app folder: holding it as a working directory would keep it busy while the
            // script is replacing files inside it.
            WorkingDirectory = Path.GetTempPath(),
        };

        // ArgumentList rather than a hand-built command line - the app folder can sit anywhere,
        // including a path with spaces in it, and this quotes each argument correctly. Invoking
        // powershell.exe directly (rather than a .cmd through cmd.exe) also avoids cmd's own
        // separate, much stranger quote-stripping rules.
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        AppLog.Info("AppUpdate", $"starting {ScriptName} to swap in the staged build and restart");
        AppLog.Flush();

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            throw new AppUpdateException(
                "Couldn't start the updater script. The new version is downloaded and unpacked in "
                + $"{PayloadDirectory} - close the app and copy what's in there over this folder to finish by hand.",
                ex);
        }
    }

    // Removes a staged update without applying it.
    public static void ClearWorkingFiles()
    {
        try
        {
            if (Directory.Exists(PayloadDirectory)) Directory.Delete(PayloadDirectory, recursive: true);

            var extractDirectory = Path.Combine(UpdateDirectory, "extracted");
            if (Directory.Exists(extractDirectory)) Directory.Delete(extractDirectory, recursive: true);

            if (Directory.Exists(UpdateDirectory))
                foreach (var zip in Directory.EnumerateFiles(UpdateDirectory, "*.zip"))
                    File.Delete(zip);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("AppUpdate", $"couldn't clear staged update files: {ex.Message}");
        }
    }

    public static bool HasStagedUpdate => File.Exists(Path.Combine(PayloadDirectory, ExeName));

    //
    // The release zip roots its contents under a "TCFModManager\" folder, but this looks for the
    // exe rather than for that name: a release packaged flat, or under a differently-named folder,
    // still installs correctly, and anything with no exe in it at all is refused outright rather
    // than copied over a working install.
    //
    private static string? FindPayloadRoot(string extractDirectory)
    {
        if (File.Exists(Path.Combine(extractDirectory, ExeName))) return extractDirectory;

        return Directory
            .EnumerateDirectories(extractDirectory, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(d => File.Exists(Path.Combine(d, ExeName)));
    }

    private static void VerifyPayload()
    {
        var stagedExe = Path.Combine(PayloadDirectory, ExeName);
        if (!File.Exists(stagedExe))
            throw new AppUpdateException($"The staged update is missing {ExeName} - nothing was changed.");

        // A self-contained single-file build is upwards of 100MB. Anything in the low kilobytes is
        // a truncated download or a stub, and is not worth copying over a working install.
        var length = new FileInfo(stagedExe).Length;
        if (length < 1024 * 1024)
            throw new AppUpdateException(
                $"The staged {ExeName} is only {length / 1024}KB, which is far too small to be a real build - nothing was changed.");
    }

    private static void EnsureAppFolderIsWritable()
    {
        var probe = Path.Combine(AppDirectory, $".tcfmm-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppUpdateException(
                $"This app can't write to its own folder ({AppDirectory}), so it can't update itself in place. "
                + "Move it somewhere outside Program Files, or download the new version from the mod page and replace it by hand.",
                ex);
        }
    }

    private static void EnsureEnoughFreeSpace(long? downloadSizeBytes)
    {
        if (downloadSizeBytes is not > 0) return;

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(AppDirectory)!);

            // The zip and the build extracted out of it are both on disk at once, and the build is
            // a good deal larger than the compressed download - four times the download size is a
            // comfortable margin over what that actually costs.
            var required = downloadSizeBytes.Value * 4;
            if (drive.AvailableFreeSpace >= required) return;

            throw new AppUpdateException(
                $"Not enough free space on {drive.Name} to download and unpack the update - "
                + $"about {required / (1024 * 1024)}MB is needed, {drive.AvailableFreeSpace / (1024 * 1024)}MB is free.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Couldn't read the drive - not a reason to block an update that would probably work.
            AppLog.Debug("AppUpdate", $"skipped the free-space check: {ex.Message}");
        }
    }

    private static string Sanitize(string version) =>
        string.Concat(version.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    //
    // Runs after this process has exited, so it can't use anything in the app. Every path it needs
    // is derived from its own location ($PSScriptRoot is the .tcfmm-update folder, whose parent is
    // the app folder), which means the only thing passed in is a process id - nothing that could be
    // mangled by quoting on the way across.
    //
    private const string ApplyScript = """
        # TCF Mod Manager - applies a downloaded update once the app itself has exited.
        # Written and started by AppUpdateInstaller; not meant to be run by hand.
        param([Parameter(Mandatory = $true)][int]$ProcessId)

        $updateDir = $PSScriptRoot
        $payloadDir = Join-Path $updateDir 'payload'
        $appDir = Split-Path $updateDir -Parent
        $exePath = Join-Path $appDir 'TCFModManager.exe'
        $logPath = Join-Path $updateDir 'apply-update.log'

        function Write-Log([string]$message) {
            "$(Get-Date -Format 'HH:mm:ss') $message" | Out-File -FilePath $logPath -Append -Encoding utf8
        }

        function Start-App {
            if (Test-Path -LiteralPath $exePath) {
                Start-Process -FilePath $exePath -WorkingDirectory $appDir -WindowStyle Normal
            }
        }

        Write-Log "waiting for TCF Mod Manager (PID $ProcessId) to exit"
        $running = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($running) { $null = $running.WaitForExit(60000) }

        if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
            Write-Log "PID $ProcessId is still running after 60s - update NOT applied, existing version untouched"
            exit 1
        }

        # The exe's file handle can outlive the process by a moment.
        Start-Sleep -Milliseconds 750

        Write-Log "copying the new build into $appDir"
        # /E and never /MIR: this lays the new build over the old one and leaves the rest of the
        # folder alone. /MIR would delete Data\ and Staging\ - the saved SPT path, install history,
        # kept mod configs and config backups all live alongside the exe.
        $output = & robocopy $payloadDir $appDir /E /R:5 /W:2 /NFL /NDL /NJH /NJS /NC /NS 2>&1
        $robocopyExit = $LASTEXITCODE
        foreach ($line in $output) { if ("$line".Trim()) { Write-Log "robocopy: $line" } }

        # Robocopy's exit code is a bitmask - 0-7 are all varieties of success, 8 and up is a real
        # failure. Not the 0-is-ok convention everything else uses.
        if ($robocopyExit -ge 8) {
            Write-Log "robocopy failed (exit $robocopyExit) - restarting the existing version"
            Start-App
            exit 1
        }

        Write-Log "update applied (robocopy exit $robocopyExit)"
        Remove-Item -LiteralPath $payloadDir -Recurse -Force -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath $updateDir -Filter '*.zip' -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue

        Start-App
        exit 0
        """;
}
