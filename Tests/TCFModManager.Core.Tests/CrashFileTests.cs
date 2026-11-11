using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public sealed class CrashFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "crashfile-" + Guid.NewGuid().ToString("N"));

    private string File_ => Path.Combine(_dir, "logs", CrashFile.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static T Thrown<T>(T exception) where T : Exception
    {
        try
        {
            throw exception;
        }
        catch (T caught)
        {
            return caught;
        }
    }

    [Fact]
    public void A_written_crash_is_taken_back_once()
    {
        var at = new DateTimeOffset(2026, 11, 11, 21, 40, 5, TimeSpan.Zero);
        var crash = Thrown(new InvalidOperationException("off the UI thread"));

        CrashFile.Write(crash, at, File_);
        var taken = CrashFile.Take(File_);

        Assert.NotNull(taken);
        Assert.Equal(at, taken.At);
        Assert.StartsWith("System.InvalidOperationException: off the UI thread", taken.Text);
        Assert.Contains(nameof(Thrown), taken.Text);
        Assert.False(File.Exists(File_));
        Assert.Null(CrashFile.Take(File_));
    }

    [Fact]
    public void No_file_means_no_crash()
    {
        Assert.Null(CrashFile.Take(File_));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a date\nSystem.Exception: x")]
    [InlineData("2026-11-11T21:40:05.0000000+00:00\n   ")]
    public void A_file_that_is_not_a_crash_is_removed_and_ignored(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, text);

        Assert.Null(CrashFile.Take(File_));
        Assert.False(File.Exists(File_));
    }

    [Fact]
    public void Writing_never_throws()
    {
        var blocker = Path.Combine(_dir, "blocker");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocker, "");

        CrashFile.Write(new IOException("x"), DateTimeOffset.Now, Path.Combine(blocker, "last-crash.txt"));
    }
}
