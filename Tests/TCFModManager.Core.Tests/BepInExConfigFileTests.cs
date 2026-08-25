using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class BepInExConfigFileTests
{
    private const string Sample = """
        ## Settings file was created by plugin SAIN v3.0.0
        ## Plugin GUID: me.sol.sain

        [Bot Difficulty]

        ## Turns the difficulty system on.
        ## Restart the raid for this to take effect.
        # Setting type: Boolean
        # Default value: true
        Enable Difficulty = false

        ## How many bots at once.
        # Setting type: Int32
        # Default value: 20
        # Acceptable value range: From 1 to 50
        Max Bots = 25

        [Presets]

        ## Which preset to load.
        # Setting type: EDifficulty
        # Default value: Normal
        # Acceptable values: Easy, Normal, Hard
        Preset = Hard
        """;

    [Fact]
    public void Parse_ReadsSectionsInFileOrder()
    {
        var file = BepInExConfigFile.Parse(Sample);

        Assert.Equal(["Bot Difficulty", "Presets"], file.Sections.Select(s => s.Name));
        Assert.Equal(["Enable Difficulty", "Max Bots"], file.Sections[0].Settings.Select(s => s.Key));
    }

    [Fact]
    public void Parse_ReadsTheAuthorsDescriptionFromTheDoubleHashLines()
    {
        var setting = Setting(Sample, "Enable Difficulty");

        Assert.Equal(
            $"Turns the difficulty system on.{Environment.NewLine}Restart the raid for this to take effect.",
            setting.Description);
    }

    [Fact]
    public void Parse_ReadsTheTypeAndDefaultBepInExRecorded()
    {
        var setting = Setting(Sample, "Enable Difficulty");

        Assert.Equal("Boolean", setting.SettingType);
        Assert.True(setting.IsBoolean);
        Assert.Equal("true", setting.DefaultValue);
        Assert.Equal("false", setting.Value);
        Assert.False(setting.IsAtDefault);
    }

    [Fact]
    public void Parse_ReadsANumericRange()
    {
        var setting = Setting(Sample, "Max Bots");

        Assert.True(setting.HasRange);
        Assert.Equal("1", setting.RangeMinimum);
        Assert.Equal("50", setting.RangeMaximum);
        Assert.False(setting.HasChoices);
    }

    [Fact]
    public void Parse_ReadsAnEnumsAcceptableValues()
    {
        var setting = Setting(Sample, "Preset");

        Assert.True(setting.HasChoices);
        Assert.Equal(["Easy", "Normal", "Hard"], setting.AcceptableValues);
        Assert.False(setting.AllowsMultipleValues);
    }

    [Fact]
    public void Parse_NotesAFlagsEnumAcceptingSeveralValuesAtOnce()
    {
        var text = """
            [General]

            # Setting type: Filters
            # Default value: None
            # Acceptable values: None, Ammo, Armor
            # Multiple values can be set at the same time.
            Filters = Ammo, Armor
            """;

        Assert.True(Setting(text, "Filters").AllowsMultipleValues);
    }

    [Fact]
    public void Parse_KeepsAnEmptyDefaultDistinctFromNoDefaultAtAll()
    {
        var text = """
            [General]

            # Setting type: String
            # Default value:
            Prefix =

            # Setting type: String
            Suffix = x
            """;

        Assert.Equal(string.Empty, Setting(text, "Prefix").DefaultValue);
        Assert.Null(Setting(text, "Suffix").DefaultValue);
    }

    [Fact]
    public void ToText_ReturnsTheFileUnchangedWhenNothingWasEdited() =>
        Assert.Equal(Sample, BepInExConfigFile.Parse(Sample).ToText());

    [Fact]
    public void ToText_RewritesOnlyTheEditedLineAndKeepsEveryComment()
    {
        var file = BepInExConfigFile.Parse(Sample);
        file.Settings.First(s => s.Key == "Max Bots").Value = "40";

        var written = file.ToText();

        Assert.Contains("Max Bots = 40", written);
        Assert.DoesNotContain("Max Bots = 25", written);
        Assert.Contains("## How many bots at once.", written);
        Assert.Contains("# Acceptable value range: From 1 to 50", written);
        Assert.Contains("## Settings file was created by plugin SAIN v3.0.0", written);

        // Every other line is untouched, so the file is the same length it was.
        Assert.Equal(Sample.Split('\n').Length, written.Split('\n').Length);
    }

    [Fact]
    public void ToText_KeepsWindowsLineEndings()
    {
        var text = "[General]\r\n\r\n# Setting type: Boolean\r\n# Default value: true\r\nEnabled = true\r\n";

        var file = BepInExConfigFile.Parse(text);
        file.Settings[0].Value = "false";

        var written = file.ToText();

        Assert.Contains("Enabled = false\r\n", written);
        Assert.DoesNotContain("\n\n", written.Replace("\r\n", "\r"));
    }

    //
    // Real .cfg files are not consistent about this. A mod author whose setting description contains
    // a bare "\n" gets it written into an otherwise CRLF file verbatim, so the file ends up with both
    // endings in it - two of the ten configs in the install this was checked against are like that.
    // A writer that picked the dominant ending would silently rewrite every one of those lines,
    // turning a one-setting edit into a diff covering most of the file.
    //
    [Fact]
    public void ToText_KeepsEachLinesOwnEndingInAFileThatMixesThem()
    {
        var text = "[General]\r\n\r\n## Two lines of description\n## written with a bare newline\r\n# Setting type: Boolean\r\nEnabled = true\r\n";

        var file = BepInExConfigFile.Parse(text);

        Assert.Equal(text, file.ToText());

        file.Settings[0].Value = "false";
        Assert.Equal(text.Replace("Enabled = true", "Enabled = false"), file.ToText());
    }

    [Fact]
    public void ToText_KeepsAFileThatDoesNotEndInANewline()
    {
        var text = "[General]\nEnabled = true";

        Assert.Equal(text, BepInExConfigFile.Parse(text).ToText());
    }

    [Fact]
    public void ToText_KeepsTheFilesOwnSpacingAroundTheEquals()
    {
        var file = BepInExConfigFile.Parse("[General]\nEnabled=true\n");
        file.Settings[0].Value = "false";

        Assert.Contains("Enabled=false", file.ToText());
    }

    [Fact]
    public void Parse_TakesTheLastOccurrenceWhenAKeyIsRepeated()
    {
        // BepInEx itself reads the file top to bottom and keeps the last value, so the last line is
        // the live one and is where an edit has to land.
        var file = BepInExConfigFile.Parse("[General]\nEnabled = true\nEnabled = false\n");

        var setting = Assert.Single(file.Sections[0].Settings);
        Assert.Equal("false", setting.Value);

        setting.Value = "true";
        Assert.Equal("[General]\nEnabled = true\nEnabled = true\n", file.ToText());
    }

    [Fact]
    public void Parse_PutsASettingWrittenBeforeAnySectionUnderItsOwnHeading()
    {
        var file = BepInExConfigFile.Parse("Loose = 1\n");

        Assert.Equal(string.Empty, file.Sections[0].Name);
        Assert.Equal("General", file.Sections[0].DisplayName);
    }

    [Fact]
    public void Parse_DoesNotAttachACommentBlockSeparatedByABlankLine()
    {
        var text = "[General]\n\n## Belongs to nothing\n\nEnabled = true\n";

        Assert.Equal(string.Empty, Setting(text, "Enabled").Description);
    }

    [Fact]
    public void ResetToDefault_PutsBackTheDefaultBepInExRecorded()
    {
        var setting = Setting(Sample, "Enable Difficulty");

        setting.ResetToDefault();

        Assert.Equal("true", setting.Value);
        Assert.True(setting.IsAtDefault);
    }

    [Fact]
    public void Revert_PutsBackWhatTheFileHeld()
    {
        var file = BepInExConfigFile.Parse(Sample);
        var setting = file.Settings.First(s => s.Key == "Preset");

        setting.Value = "Easy";
        Assert.True(setting.IsModified);
        Assert.True(file.IsModified);

        setting.Revert();

        Assert.False(setting.IsModified);
        Assert.False(file.IsModified);
    }

    [Fact]
    public void AcceptChanges_MakesTheCurrentValuesTheSavedOnes()
    {
        var file = BepInExConfigFile.Parse(Sample);
        file.Settings.First(s => s.Key == "Preset").Value = "Easy";

        file.AcceptChanges();

        Assert.False(file.IsModified);
    }

    private static BepInExConfigSetting Setting(string text, string key) =>
        BepInExConfigFile.Parse(text).Settings.First(s => s.Key == key);
}
