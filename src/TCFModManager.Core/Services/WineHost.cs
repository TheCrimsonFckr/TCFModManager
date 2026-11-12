using System.Runtime.InteropServices;

namespace TCFModManager.Core.Services;

public static class WineHost
{
    private static readonly Lazy<(string? Version, string? System, string? Release)> Info = new(Read);

    public static bool IsWine => Info.Value.Version is not null;

    public static string? Version => Info.Value.Version;

    public static string? HostSystem => Info.Value.System;

    public static string? HostRelease => Info.Value.Release;

    public static string? Description
    {
        get
        {
            if (Version is not { } version) return null;
            var host = string.Join(' ', new[] { HostSystem, HostRelease }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return host.Length > 0 ? $"Wine {version} on {host}" : $"Wine {version}";
        }
    }

    private static (string?, string?, string?) Read()
    {
        if (!OperatingSystem.IsWindows()) return (null, null, null);

        try
        {
            var ntdll = Native.GetModuleHandleW("ntdll.dll");
            if (ntdll == IntPtr.Zero) return (null, null, null);

            var versionProc = Native.GetProcAddress(ntdll, "wine_get_version");
            if (versionProc == IntPtr.Zero) return (null, null, null);

            var version = Marshal.PtrToStringAnsi(
                Marshal.GetDelegateForFunctionPointer<Native.WineGetVersion>(versionProc)());

            string? system = null, release = null;
            var hostProc = Native.GetProcAddress(ntdll, "wine_get_host_version");
            if (hostProc != IntPtr.Zero)
            {
                Marshal.GetDelegateForFunctionPointer<Native.WineGetHostVersion>(hostProc)(out var sys, out var rel);
                system = sys == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(sys);
                release = rel == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(rel);
            }

            return (string.IsNullOrWhiteSpace(version) ? "unknown" : version, system, release);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return (null, null, null);
        }
    }

    private static class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr WineGetVersion();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void WineGetHostVersion(out IntPtr sysname, out IntPtr release);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern IntPtr GetModuleHandleW(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false)]
        public static extern IntPtr GetProcAddress(IntPtr module, string procName);
    }
}
