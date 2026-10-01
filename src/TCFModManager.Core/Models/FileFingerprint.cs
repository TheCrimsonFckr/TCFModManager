using System.Security.Cryptography;

namespace TCFModManager.Core.Models;

//
// A placed file's size and SHA-256, so a later removal can tell whether the file on disk is still
// exactly what this app put there (OPEN-10-TCFResilience-DESIGN.md §6, D21). Path is the same
// forward-slash, install-relative path the record's Files list uses.
//
// SHA-256 because it is in the base library: XxHash64 would need the System.IO.Hashing package, and
// hashing is limited by the disk either way.
//
public sealed record FileFingerprint(string Path, long Size, string Sha256)
{
    // Null when the file can't be read - a fingerprint is never guessed.
    public static FileFingerprint? Compute(string fullPath, string recordedPath)
    {
        try
        {
            using var stream = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 20, FileOptions.SequentialScan);

            var size = stream.Length;
            var hash = Convert.ToHexString(SHA256.HashData(stream));

            return new FileFingerprint(recordedPath, size, hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    //
    // Whether the file at fullPath is byte-for-byte what this fingerprint describes. The size is
    // checked first, so a file that differs in length is never hashed. A file that is missing or
    // can't be read does not match.
    //
    public bool Matches(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length != Size) return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return Compute(fullPath, Path) is { } now && string.Equals(now.Sha256, Sha256, StringComparison.OrdinalIgnoreCase);
    }
}

//
// A file that was already in the install, owned by no record, when an install placed over it. The
// original is kept in the app's Data folder (D22). BackupPath is relative to the Data folder, so
// moving the app with its Data keeps it valid.
//
// InOwnFolder: the file sat inside one of the folders this install places - almost always an earlier
// copy of the same mod, installed by hand. Whether removal puts those back is stage 4's call; the
// originals are kept either way.
//
public sealed record OverwrittenFile(string Path, long Size, string Sha256, string BackupPath, bool InOwnFolder = false);
