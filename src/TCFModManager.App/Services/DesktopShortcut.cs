using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Services;

//
// The Quick play shortcut on the desktop (OPEN-43): this exe with --quick-play, its own icon.
// Written with the shell's own ShellLink object, so it is an ordinary .lnk.
//
internal static class DesktopShortcut
{
    public const string QuickPlayArgument = "--quick-play";

    public static bool IsQuickPlay(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, QuickPlayArgument, StringComparison.OrdinalIgnoreCase));

    // Writes or replaces the shortcut and returns its path. Throws what the shell or the disk threw.
    public static string CreateQuickPlay()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("The app's own path is unknown.");
        var name = string.Concat(Strings.QuickPlay_ShortcutName.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + ".lnk");

        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(exe);
            link.SetArguments(QuickPlayArgument);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe) ?? "");
            link.SetIconLocation(exe, 0);
            link.SetDescription(Strings.QuickPlay_ShortcutDescription);
            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }

        return path;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int maxPath, IntPtr findData, int flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath, int maxIconPath, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, int reserved);
        void Resolve(IntPtr hwnd, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
