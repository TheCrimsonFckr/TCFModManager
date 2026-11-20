using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TCFModManager.Core.Services;

public sealed record RunningSptProcess(int Id, DateTime StartedUtc, string ExePath, bool IsServer, bool HasWindow);

public sealed class WatchedProcess : IDisposable
{
    private const uint Synchronize = 0x00100000;
    private const uint QueryLimitedInformation = 0x1000;
    private const uint WaitObject0 = 0;

    private readonly SafeProcessHandle _handle;

    private WatchedProcess(RunningSptProcess process, SafeProcessHandle handle)
    {
        Process = process;
        _handle = handle;
        ShownWindow = process.HasWindow;
    }

    public RunningSptProcess Process { get; }

    public bool ShownWindow { get; set; }

    public static WatchedProcess? Open(RunningSptProcess process)
    {
        try
        {
            var handle = OpenProcess(Synchronize | QueryLimitedInformation, false, (uint)process.Id);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return null;
            }

            return new WatchedProcess(process, handle);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public bool TryGetExit(out int exitCode)
    {
        exitCode = 0;
        if (_handle.IsClosed || WaitForSingleObject(_handle, 0) != WaitObject0) return false;
        if (!GetExitCodeProcess(_handle, out var code)) return false;

        exitCode = unchecked((int)code);
        return true;
    }

    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle handle, out uint exitCode);
}

public static partial class SptLaunchService
{
    private static readonly ConcurrentDictionary<(int Id, DateTime StartedUtc), DateTime> StoppedByApp = new();

    /// <summary>This install's server and game processes, as they are now.</summary>
    public static IReadOnlyList<RunningSptProcess> RunningProcesses(string? installPath)
    {
        var found = new List<RunningSptProcess>();
        if (string.IsNullOrWhiteSpace(installPath)) return found;

        var server = Describe(installPath, SptLaunchTarget.Server);
        if (server.Exists)
        {
            foreach (var process in ProcessesFor(server))
                using (process)
                    if (Running(process, isServer: true, hasWindow: false) is { } running) found.Add(running);
        }

        foreach (var process in GameProcesses(installPath, sparesWindowless: false))
            using (process)
                if (Running(process, isServer: false, hasWindow: HasWindow(process)) is { } running) found.Add(running);

        return found;
    }

    public static bool WasStoppedByApp(int processId, DateTime startedUtc) =>
        StoppedByApp.ContainsKey((processId, startedUtc));

    private static RunningSptProcess? Running(Process process, bool isServer, bool hasWindow)
    {
        try
        {
            if (process.HasExited || process.MainModule?.FileName is not { } exe) return null;
            return new RunningSptProcess(process.Id, process.StartTime.ToUniversalTime(), exe, isServer, hasWindow);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static void MarkStoppedByApp(Process process)
    {
        try
        {
            var now = DateTime.UtcNow;
            foreach (var old in StoppedByApp.Where(e => now - e.Value > TimeSpan.FromMinutes(10)).Select(e => e.Key).ToList())
                StoppedByApp.TryRemove(old, out _);

            StoppedByApp[(process.Id, process.StartTime.ToUniversalTime())] = now;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
        }
    }

    private static void MarkHeadlessGamesStoppedByApp(string? installPath)
    {
        foreach (var process in GameProcesses(installPath, sparesWindowless: false))
            using (process)
                if (!HasWindow(process)) MarkStoppedByApp(process);
    }
}
