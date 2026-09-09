using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Which exe in an install root means "this machine runs a Fika headless client".
//
// It is the only thing that says so. A headless install has SPT.Server.exe, BepInEx and a game client
// exactly like a player's, so getting this wrong is not a cosmetic miss - the answer feeds the setup
// prompt, and through it which entries of a SERVED mod list this machine installs.
//
// Two wrong versions have shipped, in opposite directions, and there is a test here for each.
//
public sealed class HeadlessLauncherDetectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "tcfmm-headless-" + Guid.NewGuid().ToString("N"));

    public HeadlessLauncherDetectionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Add(string fileName) => File.WriteAllText(Path.Combine(_root, fileName), "");

    private bool IsHeadless() => SptLaunchService.TryFindHeadlessLauncherExe(_root, out _);

    //
    // The name Chris confirmed on a real headless install. An earlier version matched only
    // "*Fika*Launcher*.exe", which does not match this at all - so the Play page's headless card
    // never once appeared on the machine it exists for.
    //
    [Fact]
    public void TheHeadlessManagerIsFound()
    {
        Add("FikaHeadlessManager.exe");

        Assert.True(IsHeadless());
    }

    //
    // THE ONE THAT MATTERS. "SPT-Fika Launcher.exe" is the ordinary Fika PLAYER launcher and sits in
    // every Fika player install - and it matches "*Fika*Launcher*.exe", which was kept on as a
    // fallback after the miss above was fixed.
    //
    // Left in, it makes a player's machine look like a headless: the card appears, the setup prompt
    // asks a question that does not apply, and answering it has a server's mod list arrive stripped
    // of the mods only a player needs. Silent, and on the machine somebody is actually sitting at.
    //
    [Fact]
    public void TheOrdinaryFikaPlayerLauncherIsNotAHeadless()
    {
        Add("SPT-Fika Launcher.exe");
        Add("SPT.Launcher.exe");
        Add("SPT.Server.exe");

        Assert.False(IsHeadless());
    }

    // The pattern behind the known name still requires the word, so another spelling is found without
    // reopening the hole above.
    [Fact]
    public void AnotherHeadlessSpellingIsStillFound()
    {
        Add("Fika.Headless.Launcher.exe");

        Assert.True(IsHeadless());
    }

    // A plain SPT install says nothing about a headless, whatever else is in it.
    [Fact]
    public void APlainInstallIsNotAHeadless()
    {
        Add("SPT.Server.exe");
        Add("SPT.Launcher.exe");
        Add("EscapeFromTarkov.exe");

        Assert.False(IsHeadless());
    }

    // The name wins over the pattern, so a folder holding both resolves to the confirmed one.
    [Fact]
    public void TheKnownNameWinsOverThePattern()
    {
        Add("AAA.Headless.exe");
        Add("FikaHeadlessManager.exe");

        Assert.True(SptLaunchService.TryFindHeadlessLauncherExe(_root, out var found));
        Assert.Equal("FikaHeadlessManager.exe", Path.GetFileName(found));
    }
}
