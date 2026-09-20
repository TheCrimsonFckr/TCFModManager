using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Localization;

//
// The base every view model in this app sits on, so that the text it computes follows a language
// change without each one being wired up for it.
//
// WHAT FOLLOWS A SWITCH AND WHAT DOES NOT, because this is the rule the whole of S4 is written to:
//
//   - A COMPUTED property - PinLabel, SelectedCountLabel, a dropdown entry's Label - is re-read and
//     comes back in the new language.
//   - A string ASSIGNED TO A FIELD - StatusMessage, the line left behind by a scan or a save -
//     keeps the language it was composed in until the next action replaces it. That is deliberate:
//     it is the record of something that already happened, and rewriting it would mean re-running
//     the scan it describes.
//
// So a string that has to follow the language must be a property that recomputes, never a field
// written once.
//
// OnPropertyChanged with an empty name is WPF's "every binding on this object, re-read" - which is
// why no property here needs naming, and why adding a localized property to a view model needs no
// change to this file.
//
// Registration is WEAK and automatic. A static event holding these directly would keep every mod
// card alive for the life of the process, and the Installed page builds and discards those on
// every scan - which is also why the list prunes itself as it grows rather than only when the
// language changes.
//
public abstract class LocalizedViewModel : ObservableObject
{
    private static readonly List<WeakReference<LocalizedViewModel>> Registered = [];

    private static readonly object Gate = new();

    private static bool _hooked;

    // How many registrations since the last prune. A rescan can add hundreds of card view models
    // and drop the lot a moment later; without this the list only shrinks when somebody changes
    // language, which most sessions never do.
    private static int _sinceSweep;

    private const int SweepEvery = 256;

    protected LocalizedViewModel()
    {
        lock (Gate)
        {
            if (!_hooked)
            {
                AppLanguage.Changed += (_, _) => RefreshAll();
                _hooked = true;
            }

            Registered.Add(new WeakReference<LocalizedViewModel>(this));

            if (++_sinceSweep >= SweepEvery)
            {
                Sweep();
                _sinceSweep = 0;
            }
        }
    }

    //
    // Overridable for the view models that own a collection of things that are not themselves view
    // models - a list of plain records, say - and have to pass the word along.
    //
    protected internal virtual void RefreshText() => OnPropertyChanged(string.Empty);

    private static void RefreshAll()
    {
        List<LocalizedViewModel> live;

        lock (Gate)
        {
            live = [];

            for (var i = Registered.Count - 1; i >= 0; i--)
            {
                if (Registered[i].TryGetTarget(out var target)) live.Add(target);
                else Registered.RemoveAt(i);
            }

            _sinceSweep = 0;
        }

        // Raised on the UI thread: a binding refresh is not a background operation, and the
        // language is changed from the Options page rather than from anything off-thread.
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Notify(live));
            return;
        }

        Notify(live);
    }

    private static void Notify(List<LocalizedViewModel> live)
    {
        foreach (var target in live)
        {
            try
            {
                target.RefreshText();
            }
            catch (Exception ex)
            {
                // One view model that cannot redraw is not a reason for the rest of the window to
                // stay in the old language.
                AppLog.Warn("Language", $"couldn't refresh {target.GetType().Name}: {ex.Message}");
            }
        }

        AppLog.Debug("Language", $"refreshed {live.Count} view models");
    }

    private static void Sweep()
    {
        for (var i = Registered.Count - 1; i >= 0; i--)
        {
            if (!Registered[i].TryGetTarget(out _)) Registered.RemoveAt(i);
        }
    }
}
