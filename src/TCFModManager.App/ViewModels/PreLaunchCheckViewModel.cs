using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// How ready this install is for the server it is about to join.
public enum PreLaunchState
{
    // No server configured, or the Server map page is switched off. Nothing to say.
    NotChecking,

    // Asking.
    Checking,

    // The install has everything the server's list names, at the versions it names.
    Ready,

    // Mods to install, update or enable before this install matches the server.
    Behind,

    // The server publishes a newer revision of its list than the one held here.
    ListMoved,

    // The server was configured but did not answer, or answered and refused. Not a blocker.
    Unavailable,
}


//
// The check that runs before the game is launched: does this install match what the server expects?
//
// This is the direct replacement for something that was lost on purpose. The retired BepInEx plugin
// spoke at the splash screen, seconds before joining, which is the honest moment - a page only
// speaks when it is open, and someone can launch from the SPT launcher and join three mods behind in
// silence. Putting the check where the launch button is puts it back in front of the person at the
// moment it matters.
//
// It NEVER blocks a launch. It has no business deciding whether somebody plays: the mods it is
// comparing may not matter, the server may be wrong, and a raid held up by a warning nobody can
// override is worse than a version mismatch. It says what it found, offers to take you to the page
// that fixes it, and leaves the button working.
//
public sealed partial class PreLaunchCheckViewModel : ObservableObject
{
    private readonly ModListService _lists = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(NeedsAttention))]
    [NotifyPropertyChangedFor(nameof(Severity))]
    [NotifyPropertyChangedFor(nameof(Title))]
    private PreLaunchState _state = PreLaunchState.NotChecking;

    [ObservableProperty]
    private string _message = "";

    // The mods that would have to change for this install to match. Empty unless Behind.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutstanding))]
    private string _outstanding = "";

    public bool IsVisible => State != PreLaunchState.NotChecking;

    public bool IsBusy => State == PreLaunchState.Checking;

    public bool HasOutstanding => !string.IsNullOrWhiteSpace(Outstanding);

    // Ready and Unavailable are both "carry on" - one because everything matches, the other because
    // nothing could be checked. Neither is worth colouring.
    public bool NeedsAttention => State is PreLaunchState.Behind or PreLaunchState.ListMoved;

    public string Severity => NeedsAttention ? "Warning" : "Informational";

    public string Title => State switch
    {
        PreLaunchState.Checking => "Checking the server...",
        PreLaunchState.Ready => "Ready to join",
        PreLaunchState.Behind => "Your install doesn't match this server",
        PreLaunchState.ListMoved => "This server's mod list has changed",
        PreLaunchState.Unavailable => "Couldn't check the server",
        _ => "",
    };

    //
    // Run when the Play page appears, and again on demand.
    //
    // Deliberately not on a timer. It reads the whole install to compare against the list, which is
    // the expensive thing this app does, and the answer only changes when somebody changes something
    // - so it runs when the page is opened and when asked, not every two seconds beside the launch
    // buttons.
    //
    [RelayCommand]
    public async Task CheckAsync()
    {
        var gate = AppServices.ServerMap;

        if (!gate.IsPageEnabled || !gate.IsConfigured)
        {
            State = PreLaunchState.NotChecking;
            Message = "";
            Outstanding = "";
            return;
        }

        State = PreLaunchState.Checking;
        Outstanding = "";

        // The handshake first: it is cheap, and its list revision is what says whether the held copy
        // is still what the server is serving.
        await gate.ConnectAsync();

        var held = AppServices.ModLists.GetActiveServer() ?? gate.List;

        if (gate.Probe is not { Found: true } probe)
        {
            State = PreLaunchState.Unavailable;
            Message = held is null
                ? $"{gate.HostInput} didn't answer, so there's nothing to compare against. Launching is unaffected."
                : $"{gate.HostInput} didn't answer, so this is checked against \"{held.Name}\" as it was last"
                  + " fetched. Launching is unaffected.";

            if (held is not null) await CompareAsync(held, keepMessage: true);
            return;
        }

        //
        // A newer revision is reported before the comparison and instead of it: what is installed can
        // only be measured against a list, and the list in hand is known to be out of date. Fetching
        // it is one click away on the Server map page rather than something done behind the user's
        // back seconds before they launch.
        //
        if (probe.Hello!.HasList && held is not null && probe.Hello.ListRevision > held.Revision)
        {
            State = PreLaunchState.ListMoved;
            Message = $"The server is publishing revision {probe.Hello.ListRevision} of"
                + $" \"{probe.Hello.ListName ?? held.Name}\"; you have revision {held.Revision}."
                + " Fetch it on the Server map page and review what changed before you join.";
            return;
        }

        if (held is null)
        {
            State = probe.Hello!.HasList ? PreLaunchState.ListMoved : PreLaunchState.Ready;

            Message = probe.Hello.HasList
                ? $"{gate.ServerName} publishes a mod list you haven't fetched yet. Get it on the"
                  + " Server map page to see how your install compares."
                : $"Connected to {gate.ServerName}, which doesn't publish a mod list - so there's"
                  + " nothing to check your install against.";
            return;
        }

        await CompareAsync(held, keepMessage: false);
    }

    //
    // What would have to change for this install to match the list.
    //
    // The plan is the same one the Mod lists page shows, built the same way, so the two never
    // disagree about what is outstanding. Only the fetching and enabling half is counted: a served
    // list never disables, so a Disable action cannot appear, and a Manual one is something the user
    // has to go and get themselves - which is worth naming rather than counting as a mismatch they
    // could fix here.
    //
    private async Task CompareAsync(ModList held, bool keepMessage)
    {
        var preview = await _lists.PreviewAsync(held);

        if (preview is null)
        {
            if (!keepMessage)
            {
                State = PreLaunchState.Unavailable;
                Message = AppMessages.NoSptInstallFolder;
            }

            return;
        }

        var behind = preview.Plan.Actions
            .Where(a => a.Kind is ModListActionKind.Install or ModListActionKind.Update or ModListActionKind.Enable)
            .ToList();

        var manual = preview.Plan.Manual.ToList();

        if (behind.Count == 0 && manual.Count == 0)
        {
            if (!keepMessage)
            {
                State = PreLaunchState.Ready;
                //
                // Counted against what the server expects of THIS machine, not of everyone. A
                // headless box legitimately does not carry the player-only mods, and reporting the
                // whole list here would have it saying "18 mods, all present" while planning
                // against 12 - two numbers for one question, with the wrong one on screen.
                //
                Message = $"Your install matches \"{held.Name}\" (revision {held.Revision}) -"
                    + $" {Mods(held.EntriesApplyingTo(ModListService.MachineScope).Count())} the server expects,"
                    + " all present and at the right versions.";
            }

            return;
        }

        State = PreLaunchState.Behind;

        var parts = new List<string>();
        if (behind.Count > 0) parts.Add($"{Mods(behind.Count)} to install, update or enable");
        if (manual.Count > 0) parts.Add($"{Mods(manual.Count)} to fetch by hand");

        if (!keepMessage)
        {
            Message = $"Compared against \"{held.Name}\": {string.Join(" and ", parts)}."
                + " Apply the list on the Mod lists page to sort it, or join anyway - nothing here stops you.";
        }

        // Named rather than counted. "Three mods behind" is not something anyone can act on; the
        // names are, and they are what someone checks against what the server is actually running.
        Outstanding = string.Join(", ", behind.Concat(manual).Select(a => a.Name).Order(StringComparer.OrdinalIgnoreCase));
    }

    private static string Mods(int count) => count == 1 ? "1 mod" : $"{count} mods";
}
