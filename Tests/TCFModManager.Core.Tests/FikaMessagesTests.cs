using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-54 N32. The strings are Fika 2.4.6's own, read from Fika.Core.dll and fika-server's
// locales on a real SPT 4.1.6 install (2026-11-20).
public sealed class FikaMessagesTests
{
    private static readonly string[] CodeStrings =
    [
        "Fika is loaded! Running version: ",
        "{0} missing required mod(s), verify the following mods are present: {1}",
        "Failed to verify mods with server.",
        "Make sure that the server mod is installed!",
        "Server version check passed. Expected: >{0}, received: {1}",
        "Server version check failed. Expected: >{0}, received: {1}",
    ];

    private static readonly (string, string)[] Locale =
    [
        ("F_UI_FikaVersionError", "You are attempting to use a different version of Fika than what the raid host is running.\nClient crc: {0}\nHost crc: {1}"),
        ("F_UI_EFTVersionError", "You are attempting to use a different version of EFT than what the raid host is running.\nClient: {0}\nHost: {1}"),
        ("F_Mod_VerificationFailure", "Crc32 verification of the Fika.Core.dll failed, you will not be able to play with other people!\nCheck your log file for more information."),
        ("F_UI_FikaVersionError", "Du möchtest mit einer unterschiedlichen Fika Version als der Host beitreten. \nClient: {0}\nHost: {1}"),
        ("F_UI_EFTVersionError", "Вы пытаетесь использовать другую версию EFT отличную от хоста.\nКлиент: {0}\nХост: {1}"),
        ("F_UI_ToSShort", "Welcome to Fika!"),
    ];

    private static FikaMessages Fika() => FikaMessages.From(CodeStrings, Locale);

    [Fact]
    public void Every_message_is_found_in_Fikas_own_text()
    {
        var fika = Fika();

        Assert.Empty(fika.NotFound);
        Assert.Equal(Enum.GetValues<FikaProblem>().ToHashSet(), fika.Messages.Select(m => m.Problem).ToHashSet());
    }

    [Fact]
    public void Placeholders_come_back_in_order()
    {
        var fika = Fika();

        Assert.Equal((FikaProblem.RequiredModsMissing, (IReadOnlyList<string>)["2", "com.a.mod, com.b.mod"]),
            Unpack(fika.Match("2 missing required mod(s), verify the following mods are present: com.a.mod, com.b.mod")));
        Assert.Equal((FikaProblem.ServerTooOld, (IReadOnlyList<string>)["2.4.1", "2.3.0"]),
            Unpack(fika.Match("Server version check failed. Expected: >2.4.1, received: 2.3.0")));
        Assert.Null(fika.Match("Server version check passed. Expected: >2.4.1, received: 2.4.2"));
    }

    [Fact]
    public void A_message_over_several_lines_matches_in_any_of_Fikas_languages()
    {
        var fika = Fika();

        Assert.Equal((FikaProblem.FikaVersionDiffers, (IReadOnlyList<string>)["1A2B3C", "4D5E6F"]),
            Unpack(fika.Match("You are attempting to use a different version of Fika than what the raid host is running.\r\nClient crc: 1A2B3C\r\nHost crc: 4D5E6F")));
        Assert.Equal((FikaProblem.FikaVersionDiffers, (IReadOnlyList<string>)["1A2B3C", "4D5E6F"]),
            Unpack(fika.Match("Du möchtest mit einer unterschiedlichen Fika Version als der Host beitreten.\nClient: 1A2B3C\nHost: 4D5E6F")));
        Assert.Equal(FikaProblem.EftVersionDiffers,
            fika.Match("Вы пытаетесь использовать другую версию EFT отличную от хоста.\nКлиент: 0.16.9.5\nХост: 0.16.9.6")?.Problem);
        Assert.Equal(FikaProblem.FikaAltered,
            fika.Match("Crc32 verification of the Fika.Core.dll failed, you will not be able to play with other people!\nCheck your log file for more information.")?.Problem);
    }

    [Fact]
    public void A_message_the_installed_Fika_no_longer_has_is_named_not_guessed()
    {
        var fika = FikaMessages.From(["Fika is loaded! Running version: "], []);

        Assert.Equal(Enum.GetValues<FikaProblem>().Length, fika.NotFound.Count);
        Assert.Null(fika.Match("Failed to verify mods with server."));
    }

    [Fact]
    public void Diagnose_turns_a_Fika_line_in_the_BepInEx_log_into_a_card()
    {
        var fikaMod = new DiagnosedMod("Fika", "Fika", Path.Combine("plugins", "Fika"), InstalledModTarget.Client, "com.fika.core");
        var mods = LogModLocator.From([(fikaMod, ["Fika.Core"], [])]);
        var log = LogEntries.Parse(LogKind.BepInEx, "LogOutput.log",
        [
            "[Info   : Fika.Core] Fika is loaded! Running version: 2.4.6.0",
            "[Error  : Fika.Core] Server version check failed. Expected: >2.4.1, received: 2.3.0",
        ]);

        var finding = Assert.Single(LogDiagnoser.Diagnose(new LogFileSet(null, null, null, null, false), [], log, [], mods, Fika()).Findings);

        Assert.Equal(LogFindingKind.FikaProblem, finding.Kind);
        Assert.Equal(["ServerTooOld", "2.4.1", "2.3.0"], finding.Args);
        Assert.Equal(fikaMod, finding.Mod);
    }

    private static (FikaProblem, IReadOnlyList<string>)? Unpack((FikaProblem Problem, IReadOnlyList<string> Args)? hit) =>
        hit is { } h ? (h.Problem, h.Args) : null;
}
