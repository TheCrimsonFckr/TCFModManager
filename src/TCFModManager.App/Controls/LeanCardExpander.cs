using System.Windows.Automation;
using System.Windows.Automation.Peers;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Controls;

//
// A ui:CardExpander for long lists of rows (Installed Cards and List, Footprint, Dependencies) that
// keeps Windows UI Automation from walking into a row's insides while it is collapsed.
//
// Whenever any UI Automation client is running on the machine - a screen reader, Voice Access, or
// one of the many Windows 11 tools and utilities that watch the foreground window - WPF updates an
// automation peer for every element in the window after each layout pass. A trace of the Installed
// List view (2026-10-04) put that at 30-50% of the time it takes to build a list of rows, with or
// without the app that first showed it running. Collapsed, a row now reports itself as a single
// expandable item named after its mod (AutomationProperties.Name on each usage); expanded, it
// exposes everything inside as a normal CardExpander would. So a screen reader can still find and
// open every row - it just can't reach the buttons in a row's header without opening it first.
//
internal sealed class LeanCardExpander : CardExpander
{
    public LeanCardExpander()
    {
        // The theme's CardExpander style, as the base class gets by default. A style set in XAML,
        // which every current usage has, replaces this.
        SetResourceReference(StyleProperty, typeof(CardExpander));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LeanPeer(this);

    protected override void OnExpanded()
    {
        base.OnExpanded();
        ResetPeerChildren();
    }

    protected override void OnCollapsed()
    {
        base.OnCollapsed();
        ResetPeerChildren();
    }

    private void ResetPeerChildren() => UIElementAutomationPeer.FromElement(this)?.ResetChildrenCache();

    private sealed class LeanPeer(LeanCardExpander owner) : ExpanderAutomationPeer(owner)
    {
        protected override List<AutomationPeer>? GetChildrenCore() =>
            owner.IsExpanded ? base.GetChildrenCore() : null;
    }
}
