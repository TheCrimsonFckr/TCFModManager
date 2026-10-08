using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Behaviors;

//
// Every ui:CardExpander in the app grows and shrinks as it opens and closes (OPEN-26 §2.3, R5),
// instead of its height snapping - so the cards and rows below it slide rather than jump.
//
// WPF-UI's template already animates the body: on open it is made visible at once and slid down
// from behind the header over 0.333s; on close it slides back up over 0.167s and is collapsed at
// 0.2s. Only the card's height snapped - to full size on the first frame of opening, and back to
// the header at 0.2s when closing. This animates the card's Height between the two over
// Motion.Resize and then lets go of it, so the card is back to sizing itself when it finishes and
// anything that changes its content afterwards is free to.
//
// Opening, the size to grow to isn't known until the body has been made visible and measured,
// which happens on the next frame. So the card is held at the height it had, and the grow starts
// once layout has run. The body is measured at its natural height even while the card is held:
// the template's rows are Auto, and Grid measures Auto rows without a height limit.
//
// Class handlers on Expander's own Expanded/Collapsed events rather than a style setter, so every
// CardExpander gets it - the shared row styles, the update dialog, Help, and any added later -
// and nothing holds a reference to individual cards (see AppTheme.HookButtonPressFeedback).
//
// Needs the template's part names, ToggleButtonBorder and ContentPresenterBorder (WPF-UI 4.3's
// DefaultUiCardExpanderStyle). A card without them, one not on screen, and every card while
// Windows' Animation effects is off, just snaps as before.
//
internal static class ExpanderMotion
{
    // Bumped by every open and close, so a grow scheduled by one that was overtaken does nothing.
    private static readonly DependencyProperty GenerationProperty = DependencyProperty.RegisterAttached(
        "Generation", typeof(int), typeof(ExpanderMotion), new PropertyMetadata(0));

    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        var handler = new RoutedEventHandler(OnToggled);
        EventManager.RegisterClassHandler(typeof(CardExpander), Expander.ExpandedEvent, handler, true);
        EventManager.RegisterClassHandler(typeof(CardExpander), Expander.CollapsedEvent, handler, true);
    }

    private static void OnToggled(object sender, RoutedEventArgs e)
    {
        // Expanded and Collapsed bubble, so a card nested in another card's body reaches the outer
        // one's handler too - with the outer card as sender.
        if (!ReferenceEquals(sender, e.OriginalSource) || sender is not CardExpander card) return;

        var generation = (int)card.GetValue(GenerationProperty) + 1;
        card.SetValue(GenerationProperty, generation);

        if (!Motion.Enabled || !card.IsLoaded || !card.IsVisible
            || card.Template?.FindName("ToggleButtonBorder", card) is not FrameworkElement header
            || card.Template.FindName("ContentPresenterBorder", card) is not FrameworkElement body)
        {
            Release(card);
            return;
        }

        // ActualHeight is wherever a grow or shrink still running has got to, so turning round
        // part way carries on from there.
        var from = card.ActualHeight;

        if (card.IsExpanded)
        {
            Hold(card, from);
            GrowWhenMeasured(card, generation, header, body, triesLeft: 3);
        }
        else
        {
            Animate(card, generation, from, header.ActualHeight);
        }
    }

    private static void GrowWhenMeasured(CardExpander card, int generation, FrameworkElement header, FrameworkElement body, int triesLeft)
    {
        // Loaded comes after Render, so by then the template's storyboard has made the body visible
        // and layout has measured it.
        card.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if ((int)card.GetValue(GenerationProperty) != generation) return;

            if (body.Visibility != Visibility.Visible || body.DesiredSize.Height <= 0)
            {
                if (triesLeft > 1) GrowWhenMeasured(card, generation, header, body, triesLeft - 1);
                else Release(card);
                return;
            }

            Animate(card, generation, card.ActualHeight, header.ActualHeight + body.DesiredSize.Height);
        });
    }

    private static void Animate(CardExpander card, int generation, double from, double to)
    {
        if (Math.Abs(from - to) < 1)
        {
            Release(card);
            return;
        }

        // Snapped to whole pixels, so the cards and rows it pushes never sit between them mid-grow.
        var animation = new SnappedDoubleAnimation
        {
            From = from,
            To = to,
            Duration = Motion.Of(Motion.Resize),
            EasingFunction = Motion.GlideEase,
            Scale = VisualTreeHelper.GetDpi(card).DpiScaleY,
        };
        animation.Completed += (_, _) =>
        {
            if ((int)card.GetValue(GenerationProperty) == generation) Release(card);
        };
        card.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }

    private static void Hold(CardExpander card, double height) =>
        card.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(height, height, new Duration(TimeSpan.Zero)));

    private static void Release(CardExpander card) =>
        card.BeginAnimation(FrameworkElement.HeightProperty, null);
}
