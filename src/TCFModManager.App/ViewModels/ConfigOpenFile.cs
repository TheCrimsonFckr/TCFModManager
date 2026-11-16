using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public sealed class ConfigOpenFile
{
    private BepInExConfigFile? _parsed;

    private string? _parsedText;

    public ConfigOpenFile(ConfigEntryViewModel entry, ModConfigDocument loaded)
    {
        Entry = entry;
        Loaded = loaded;
        Text = loaded.Text;
    }

    public ConfigEntryViewModel Entry { get; internal set; }

    public ModConfigDocument Loaded { get; private set; }

    public string Text { get; set; }

    public bool IsDirty => !string.Equals(Text, Loaded.Text, StringComparison.Ordinal);

    public bool IsCfg => Entry.Entry.Format == ModConfigFormat.BepInExCfg;

    public BepInExConfigFile? Parsed
    {
        get
        {
            if (!IsCfg) return null;
            if (_parsed is null || !string.Equals(_parsedText, Text, StringComparison.Ordinal))
            {
                _parsed = BepInExConfigFile.Parse(Text);
                _parsedText = Text;
            }

            return _parsed;
        }
    }

    public int ChangedCount => Parsed?.ChangedFromDefaultCount ?? 0;

    public int SettingCount => Parsed?.Settings.Count ?? 0;

    public bool HasSetting(string term) =>
        Parsed?.Settings.Any(s => s.Key.Contains(term, StringComparison.OrdinalIgnoreCase)) == true;

    public void Commit()
    {
        if (_parsed is null || !string.Equals(_parsedText, Text, StringComparison.Ordinal)) return;

        Text = _parsed.ToText();
        _parsedText = Text;
    }

    public void Reread(ModConfigDocument loaded)
    {
        Loaded = loaded;
        Text = loaded.Text;
    }

    public void Saved(ModConfigDocument saved) => Loaded = saved;

    public void Revert() => Text = Loaded.Text;
}
