using System.IO;
using System.Runtime.InteropServices;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// OPEN-73: files and folders sent to the Recycle Bin one at a time, so one that is in use leaves only
// itself behind. A drive with no Recycle Bin deletes them, as Explorer does.
//
public static class RecycleBin
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct operation);

    public static IReadOnlyList<string> Send(IEnumerable<string> paths)
    {
        var left = new List<string>();
        foreach (var path in paths)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) continue;

            var operation = new ShFileOpStruct
            {
                wFunc = FoDelete,
                pFrom = Path.GetFullPath(path) + "\0",
                fFlags = FofAllowUndo | FofNoConfirmation | FofNoErrorUi | FofSilent,
            };

            var result = SHFileOperation(ref operation);
            if (result != 0 || operation.fAnyOperationsAborted || File.Exists(path) || Directory.Exists(path))
            {
                AppLog.Warn("Diagnose", $"couldn't recycle {path} (0x{result:X})");
                left.Add(path);
            }
        }

        return left;
    }
}
