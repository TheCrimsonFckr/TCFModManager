using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

//
// The heading a config file sits under in the Configs list. The list is grouped by where the files
// live rather than only chipped per row, so the client/server split is the shape of the list itself
// and not something the reader has to piece together row by row.
//
// A record, and derived fresh from each entry rather than held in a list: WPF groups by comparing
// the values a property returns, so value equality is what puts every client config under one
// heading. Grouping this way rather than building nested ItemsControls is also what keeps the list
// one ListBox, so selection is a single selection across the whole thing and arrow keys walk it.
//
public sealed record ConfigSectionHeader(ModConfigSource Source)
{
    public string Title => Source switch
    {
        ModConfigSource.Client => "Client",
        ModConfigSource.Server => "Server",
        ModConfigSource.Framework => "BepInEx",
        _ => "Unclaimed",
    };

    // The folder the whole section lives in, so every row underneath can leave it off its own path.
    public string Location => Source == ModConfigSource.Server ? "user\\mods" : "BepInEx\\config";

    public string Glyph => Source switch
    {
        ModConfigSource.Client => "PuzzlePiece24",
        ModConfigSource.Server => "Folder24",
        ModConfigSource.Framework => "Settings24",
        _ => "QuestionCircle24",
    };

    // Sections are shown in this order regardless of how the entries arrived.
    public int Rank => Source switch
    {
        ModConfigSource.Client => 0,
        ModConfigSource.Server => 1,
        ModConfigSource.Framework => 2,
        _ => 3,
    };
}
