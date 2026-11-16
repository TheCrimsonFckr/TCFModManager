using System.Text;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class BepInExConfigValuesTests
{
    private static readonly string LiveFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "BepInExConfigs");

    public static TheoryData<string> LiveFiles() =>
    [
        "BepInEx.cfg",
        "com.chazut.orbit.cfg",
        "com.dark.spt.showmethemoney.cfg",
        "com.dustinmoon78.gamepanelhudcompass.cfg",
        "com.tyfon.uifixes.cfg",
        "com.vultify.nobarrelshadow.cfg",
    ];

    [Theory]
    [MemberData(nameof(LiveFiles))]
    public void LiveFile_WritesBackByteForByte(string name)
    {
        var path = Path.Combine(LiveFolder, name);
        var original = File.ReadAllBytes(path);
        var file = BepInExConfigFile.Parse(ModConfigStore.Load(path).Text);

        foreach (var setting in file.Settings) setting.Value = setting.Value;

        Assert.Equal(original, Encoding.UTF8.GetBytes(file.ToText()));
    }

    [Theory]
    [MemberData(nameof(LiveFiles))]
    public void LiveFile_EveryValueReadsAsItsKind(string name)
    {
        var file = Live(name);

        foreach (var setting in file.Settings)
        {
            var readable = setting.Kind switch
            {
                BepInExValueKind.Boolean => BepInExConfigValues.TryReadBoolean(setting.Value, out _),
                BepInExValueKind.Integer => BepInExConfigValues.TryReadInteger(setting.Value, out _),
                BepInExValueKind.Number => BepInExConfigValues.TryReadNumber(setting.Value, out _),
                BepInExValueKind.Choice => BepInExConfigValues.MatchChoice(setting, setting.Value) is not null,
                BepInExValueKind.Color => BepInExConfigValues.TryReadColor(setting.Value, out _),
                BepInExValueKind.Shortcut => BepInExShortcut.TryParse(setting.Value, out _),
                _ => true,
            };

            Assert.True(readable, $"{setting.Section} / {setting.Key} = {setting.Value} ({setting.Kind})");
        }
    }

    [Fact]
    public void LiveFile_BlankKeyIsTheSameAsADefaultOfNone()
    {
        var file = Live("com.tyfon.uifixes.cfg");
        var blank = file.Settings.Where(s => s.Kind == BepInExValueKind.Shortcut && s.Value.Length == 0 && s.DefaultValue == "None").ToList();

        Assert.NotEmpty(blank);
        Assert.All(blank, s => Assert.False(s.DiffersFromDefault));
    }

    [Fact]
    public void LiveFile_DecimalZeroIsTheSameAsADefaultOfZeroPointZero()
    {
        var setting = Live("com.dark.spt.showmethemoney.cfg").Settings.Single(s => s.Key == "Tool-Tip delay");

        Assert.Equal(BepInExValueKind.Number, setting.Kind);
        Assert.Equal("0", setting.Value);
        Assert.Equal("0.0", setting.DefaultValue);
        Assert.False(setting.DiffersFromDefault);
    }

    [Fact]
    public void LiveFile_KindsComeFromTheRecordedType()
    {
        Assert.Equal(BepInExValueKind.Flags, Live("BepInEx.cfg").Settings.First(s => s.Key == "LogLevels").Kind);

        var orbit = Live("com.chazut.orbit.cfg");
        Assert.Equal(BepInExValueKind.Shortcut, orbit.Settings.Single(s => s.Key == "Capture door shortcut").Kind);

        var compass = Live("com.dustinmoon78.gamepanelhudcompass.cfg");
        Assert.Contains(compass.Settings, s => s.SettingType == "Vector2" && s.Kind == BepInExValueKind.Text);
        Assert.Contains(compass.Settings, s => s.SettingType == "FontStyles" && s.Kind == BepInExValueKind.Flags);
        Assert.Contains(compass.Settings, s => s.Kind == BepInExValueKind.Color);

        Assert.Contains(Live("com.vultify.nobarrelshadow.cfg").Settings, s => s.SettingType is null && s.Kind == BepInExValueKind.Text);
    }

    [Fact]
    public void ChangedFromDefaultCount_CountsOnlyRealChanges()
    {
        var file = BepInExConfigFile.Parse(Cfg(("Boolean", "true", "True"), ("Int32", "5", "6"), ("KeyboardShortcut", "None", "")));

        Assert.Equal(1, file.ChangedFromDefaultCount);
    }

    [Fact]
    public void DiffersFromDefault_IsFalseWithoutADefault()
    {
        var setting = BepInExConfigFile.Parse("[A]\nKey = anything\n").Settings.Single();

        Assert.False(setting.DiffersFromDefault);
    }

    [Theory]
    [InlineData("Boolean", "true", "True", true)]
    [InlineData("Boolean", "true", "false", false)]
    [InlineData("Int32", "25", "025", true)]
    [InlineData("Int32", "25", "26", false)]
    [InlineData("Single", "0.1", "0.100000001", true)]
    [InlineData("Single", "1", "1.0", true)]
    [InlineData("Single", "0.66", "0.8045313", false)]
    [InlineData("Double", "1", "1.00", true)]
    [InlineData("Decimal", "0", "0.0", true)]
    [InlineData("Color", "FF0000FF", "ff0000", true)]
    [InlineData("Color", "FF0000FF", "#F00", true)]
    [InlineData("Color", "FF0000FF", "FF000080", false)]
    [InlineData("KeyboardShortcut", "", "None", true)]
    [InlineData("KeyboardShortcut", "H + LeftControl + LeftShift", "H+LeftShift+LeftControl", true)]
    [InlineData("KeyboardShortcut", "H + LeftControl", "LeftControl + H", false)]
    [InlineData("KeyboardShortcut", "Nonsense", "", true)]
    [InlineData("String", "abc", "ABC", false)]
    public void SameValue_ComparesByType(string type, string left, string right, bool same)
    {
        var setting = BepInExConfigFile.Parse(Cfg((type, left, left))).Settings.Single();

        Assert.Equal(same, BepInExConfigValues.SameValue(setting, left, right));
    }

    [Fact]
    public void Choice_IgnoresCaseForEnumsButNotForStrings()
    {
        var enumSetting = Choice("EDifficulty", "Easy, Normal, Hard", "Hard");
        var stringSetting = Choice("String", "Use Global, Satellite", "Satellite");

        Assert.True(BepInExConfigValues.SameValue(enumSetting, "Hard", "hard"));
        Assert.Equal("Hard", BepInExConfigValues.MatchChoice(enumSetting, "hard"));
        Assert.False(BepInExConfigValues.SameValue(stringSetting, "Satellite", "satellite"));
        Assert.Null(BepInExConfigValues.MatchChoice(stringSetting, "satellite"));
        Assert.Equal("Use Global", BepInExConfigValues.MatchChoice(stringSetting, " Use Global "));
    }

    [Fact]
    public void Flags_ReadAndWriteInDeclaredOrder()
    {
        var setting = Flags("None, Quests, Extracts, Airdrops, BTR", "Extracts");

        Assert.Equal(BepInExValueKind.Flags, setting.Kind);
        Assert.Equal(["BTR", "Quests"], BepInExConfigValues.ReadFlags(setting, "btr, Quests, BTR"));
        Assert.Empty(BepInExConfigValues.ReadFlags(setting, "None"));
        Assert.Equal("Quests, BTR", BepInExConfigValues.FormatFlags(setting, ["BTR", "quests"]));
        Assert.Equal("None", BepInExConfigValues.FormatFlags(setting, []));
        Assert.True(BepInExConfigValues.SameValue(setting, "Quests, BTR", "BTR, quests"));
        Assert.True(BepInExConfigValues.SameValue(setting, "None", ""));
    }

    [Fact]
    public void FormatNumber_WritesLikeBepInEx()
    {
        var single = BepInExConfigFile.Parse(Cfg(("Single", "1", "1"))).Settings.Single();
        var integer = BepInExConfigFile.Parse(Cfg(("Int32", "1", "1"))).Settings.Single();
        var dec = BepInExConfigFile.Parse(Cfg(("Decimal", "1", "1"))).Settings.Single();

        Assert.Equal("1", BepInExConfigValues.FormatNumber(single, 1m));
        Assert.Equal("0.1", BepInExConfigValues.FormatNumber(single, 0.1m));
        Assert.Equal("0.8045313", BepInExConfigValues.FormatNumber(single, 0.8045313m));
        Assert.Equal("7", BepInExConfigValues.FormatNumber(integer, 7m));
        Assert.Equal("0.25", BepInExConfigValues.FormatNumber(dec, 0.25m));
    }

    [Fact]
    public void Clamp_KeepsToTheRangeAndWholeNumbersForIntegers()
    {
        var ranged = BepInExConfigFile.Parse("""
            [A]
            # Setting type: Int32
            # Default value: 20
            # Acceptable value range: From 1 to 50
            Max Bots = 25
            """).Settings.Single();
        var unranged = BepInExConfigFile.Parse(Cfg(("Byte", "1", "1"))).Settings.Single();

        Assert.Equal(50m, BepInExConfigValues.Clamp(ranged, 99m));
        Assert.Equal(1m, BepInExConfigValues.Clamp(ranged, -3m));
        Assert.Equal(3m, BepInExConfigValues.Clamp(ranged, 2.5m));
        Assert.Equal(255m, BepInExConfigValues.Clamp(unranged, 300m));
        Assert.True(BepInExConfigValues.TryGetRange(ranged, out var min, out var max));
        Assert.Equal((1m, 50m), (min, max));
    }

    [Fact]
    public void Color_ReadsShortAndLongHexAndWritesEightDigits()
    {
        Assert.True(BepInExConfigValues.TryReadColor("#4c1a", out var shortWithAlpha));
        Assert.Equal(new BepInExColor(0x44, 0xCC, 0x11, 0xAA), shortWithAlpha);
        Assert.True(BepInExConfigValues.TryReadColor("FF4C1ACC", out var full));
        Assert.Equal("FF4C1ACC", BepInExConfigValues.FormatColor(full));
        Assert.True(BepInExConfigValues.TryReadColor("123456", out var opaque));
        Assert.Equal("123456FF", BepInExConfigValues.FormatColor(opaque));
        Assert.False(BepInExConfigValues.TryReadColor("red", out _));
        Assert.False(BepInExConfigValues.TryReadColor("12345", out _));
    }

    [Fact]
    public void Shortcut_SortsModifiersByKeyCodeLikeBepInEx()
    {
        var shortcut = BepInExShortcut.Create("H", ["LeftControl", "LeftShift", "H", "LeftShift"]);

        Assert.Equal("H + LeftShift + LeftControl", shortcut.Format());
        Assert.Equal("H", shortcut.MainKey);
    }

    [Fact]
    public void Shortcut_ReadsEverySeparatorBepInExSplitsOn()
    {
        Assert.True(BepInExShortcut.TryParse("F8;LeftControl", out var a));
        Assert.True(BepInExShortcut.TryParse("F8 | LeftControl", out var b));
        Assert.True(BepInExShortcut.TryParse("F8,LeftControl", out var c));

        Assert.Equal("F8 + LeftControl", a.Format());
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Shortcut_EmptyAndNoneAreBothNoKey()
    {
        Assert.True(BepInExShortcut.TryParse("", out var blank));
        Assert.True(BepInExShortcut.TryParse("None", out var none));
        Assert.True(BepInExShortcut.TryParse("None + LeftControl", out var noMain));

        Assert.True(blank.IsEmpty);
        Assert.True(none.IsEmpty);
        Assert.True(noMain.IsEmpty);
        Assert.Equal(string.Empty, none.Format());
    }

    [Fact]
    public void Shortcut_AnUnknownNameIsRefusedAndReadsAsNoKey()
    {
        Assert.False(BepInExShortcut.TryParse("H + Ctrl", out _));
        Assert.False(BepInExShortcut.TryParse("h", out _));
        Assert.True(BepInExShortcut.Read("H + Ctrl").IsEmpty);
        Assert.Throws<ArgumentException>(() => BepInExShortcut.Create("Ctrl", []));
    }

    [Fact]
    public void Shortcut_ANumberIsReadAsItsKeyCode()
    {
        Assert.True(BepInExShortcut.TryParse("104", out var shortcut));
        Assert.Equal("H", shortcut.MainKey);
    }

    [Fact]
    public void Shortcut_SameKeysIgnoresWhichOneIsMain()
    {
        var a = BepInExShortcut.Read("H + LeftControl");
        var b = BepInExShortcut.Read("LeftControl + H");

        Assert.NotEqual(a, b);
        Assert.True(a.UsesSameKeys(b));
        Assert.False(a.UsesSameKeys(BepInExShortcut.Read("H")));
    }

    [Fact]
    public void UnityKeyCodes_AliasesShareAValueAndTheFirstNameIsUsed()
    {
        Assert.True(UnityKeyCodes.TryGetValue("LeftCommand", out var command));
        Assert.True(UnityKeyCodes.TryGetValue("LeftApple", out var apple));
        Assert.Equal(310, command);
        Assert.Equal(command, apple);
        Assert.Equal("LeftMeta", UnityKeyCodes.NameOf(310));
        Assert.True(UnityKeyCodes.TryGetValue("Joystick8Button19", out var lastJoystick));
        Assert.Equal(509, lastJoystick);
        Assert.True(BepInExShortcut.Read("H + LeftCommand").Equals(BepInExShortcut.Read("H + LeftApple")));
    }

    [Theory]
    [InlineData("D1", "Alpha1")]
    [InlineData("NumPad5", "Keypad5")]
    [InlineData("LeftCtrl", "LeftControl")]
    [InlineData("Return", "Return")]
    [InlineData("Enter", "Return")]
    [InlineData("Next", "PageDown")]
    [InlineData("OemTilde", "BackQuote")]
    [InlineData("Oem3", "BackQuote")]
    [InlineData("Add", "KeypadPlus")]
    [InlineData("Apps", "Menu")]
    [InlineData("F15", "F15")]
    public void UnityKeyCodes_MapsWpfKeys(string wpf, string unity)
    {
        Assert.True(UnityKeyCodes.TryFromWpfKey(wpf, out var mapped));
        Assert.Equal(unity, mapped);
    }

    [Theory]
    [InlineData("LWin")]
    [InlineData("RWin")]
    [InlineData("Oem8")]
    [InlineData("OemBackslash")]
    [InlineData("F16")]
    [InlineData("System")]
    [InlineData("PrintScreen")]
    [InlineData("VolumeUp")]
    public void UnityKeyCodes_RefusesKeysItCantBeSureOf(string wpf)
    {
        Assert.False(UnityKeyCodes.TryFromWpfKey(wpf, out _));
    }

    [Fact]
    public void UnityKeyCodes_EveryMappedWpfKeyIsARealKeyCode()
    {
        foreach (var wpf in WpfKeyNames)
        {
            if (UnityKeyCodes.TryFromWpfKey(wpf, out var unity)) Assert.True(UnityKeyCodes.IsKnown(unity), $"{wpf} -> {unity}");
        }
    }

    [Theory]
    [InlineData("Left", "Mouse0")]
    [InlineData("Right", "Mouse1")]
    [InlineData("Middle", "Mouse2")]
    [InlineData("XButton1", "Mouse3")]
    [InlineData("XButton2", "Mouse4")]
    public void UnityKeyCodes_MapsWpfMouseButtons(string wpf, string unity)
    {
        Assert.True(UnityKeyCodes.TryFromWpfMouseButton(wpf, out var mapped));
        Assert.Equal(unity, mapped);
    }

    [Fact]
    public void Keybinds_GroupsSettingsThatUseTheSameKeys()
    {
        var one = BepInExConfigFile.Parse(Cfg(("KeyboardShortcut", "H", "H + LeftControl"), ("KeyboardShortcut", "R", "R")));
        var two = BepInExConfigFile.Parse(Cfg(("KeyboardShortcut", "H", "LeftControl + H"), ("KeyboardShortcut", "None", ""), ("KeyboardShortcut", "H", "H")));

        var groups = BepInExKeybinds.Group(BepInExKeybinds.Collect("one", one).Concat(BepInExKeybinds.Collect("two", two)));

        var clash = Assert.Single(groups, g => g.IsClash);
        Assert.Equal(["one", "two"], clash.Keybinds.Select(k => k.Owner));
        Assert.Same(clash, groups[0]);
        Assert.Equal(3, groups.Count);
        Assert.DoesNotContain(groups, g => g.Shortcut.IsEmpty);
    }

    private static BepInExConfigFile Live(string name) =>
        BepInExConfigFile.Parse(ModConfigStore.Load(Path.Combine(LiveFolder, name)).Text);

    private static string Cfg(params (string Type, string Default, string Value)[] settings)
    {
        var text = new StringBuilder("[A]\n");
        for (var i = 0; i < settings.Length; i++)
        {
            text.Append($"# Setting type: {settings[i].Type}\n# Default value: {settings[i].Default}\nKey{i} = {settings[i].Value}\n\n");
        }

        return text.ToString();
    }

    private static BepInExConfigSetting Choice(string type, string values, string value) =>
        BepInExConfigFile.Parse($"[A]\n# Setting type: {type}\n# Default value: {value}\n# Acceptable values: {values}\nKey = {value}\n").Settings.Single();

    private static BepInExConfigSetting Flags(string values, string value) =>
        BepInExConfigFile.Parse($"[A]\n# Setting type: MarkerSet\n# Default value: {value}\n# Acceptable values: {values}\n# Multiple values can be set at the same time by separating them with , (e.g. Debug, Warning)\nKey = {value}\n").Settings.Single();

    private static readonly string[] WpfKeyNames =
    [
        "Cancel", "Back", "Tab", "LineFeed", "Clear", "Enter", "Return", "Pause", "Capital", "CapsLock", "Escape", "Space",
        "PageUp", "Prior", "Next", "PageDown", "End", "Home", "Left", "Up", "Right", "Down", "Select", "Print", "Execute",
        "PrintScreen", "Snapshot", "Insert", "Delete", "Help", "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W",
        "X", "Y", "Z", "LWin", "RWin", "Apps", "Sleep", "NumPad0", "NumPad1", "NumPad2", "NumPad3", "NumPad4", "NumPad5",
        "NumPad6", "NumPad7", "NumPad8", "NumPad9", "Multiply", "Add", "Separator", "Subtract", "Decimal", "Divide", "F1",
        "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "F13", "F14", "F15", "F16", "F17", "F18",
        "F19", "F20", "F21", "F22", "F23", "F24", "NumLock", "Scroll", "LeftShift", "RightShift", "LeftCtrl", "RightCtrl",
        "LeftAlt", "RightAlt", "Oem1", "OemSemicolon", "OemPlus", "OemComma", "OemMinus", "OemPeriod", "Oem2",
        "OemQuestion", "Oem3", "OemTilde", "Oem4", "OemOpenBrackets", "Oem5", "OemPipe", "Oem6", "OemCloseBrackets",
        "Oem7", "OemQuotes", "Oem8", "Oem102", "OemBackslash", "System",
    ];
}
