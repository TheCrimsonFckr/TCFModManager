using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Views;

public enum TourAction
{
    Next,
    Back,
    Skip,
    Close,
}

public partial class TourCard : UserControl
{
    private const double Gap = 10;
    private const double ShadowLeft = 8;
    private const double ShadowTop = 8;
    private const double ShadowBottom = 16;

    private bool _wanted;
    private bool _suspended;

    public TourCard()
    {
        InitializeComponent();
        Popup.CustomPopupPlacementCallback = Place;
    }

    public event EventHandler<TourAction>? Acted;

    public void Show(FrameworkElement target, string title, string body, int position, int count, bool canGoBack, bool isLast)
    {
        TitleText.Text = title;
        BodyText.Text = body;
        CountText.Text = string.Format(Strings.Tour_StepFormat, position, count);
        BackButton.Visibility = canGoBack ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = isLast ? Strings.Tour_Done : Strings.Tour_Next;

        Popup.PopupAnimation = Motion.Enabled ? PopupAnimation.Fade : PopupAnimation.None;
        Popup.PlacementTarget = target;
        _wanted = true;
        Apply();
        Reposition();
    }

    public void Hide()
    {
        _wanted = false;
        Apply();
        Popup.PlacementTarget = null;
    }

    public void Suspend(bool suspended)
    {
        _suspended = suspended;
        Apply();
    }

    public void Reposition()
    {
        if (!Popup.IsOpen) return;

        var offset = Popup.HorizontalOffset;
        Popup.HorizontalOffset = offset + 0.01;
        Popup.HorizontalOffset = offset;
    }

    private void Apply() => Popup.IsOpen = _wanted && !_suspended;

    private static CustomPopupPlacement[] Place(Size popup, Size target, Point offset)
    {
        var width = popup.Width - ShadowLeft * 2;
        var height = popup.Height - ShadowTop - ShadowBottom;

        return
        [
            new(new Point(-ShadowLeft, target.Height + Gap - ShadowTop), PopupPrimaryAxis.Horizontal),
            new(new Point(target.Width - width - ShadowLeft, target.Height + Gap - ShadowTop), PopupPrimaryAxis.Horizontal),
            new(new Point(-ShadowLeft, -height - Gap - ShadowTop), PopupPrimaryAxis.Horizontal),
            new(new Point(target.Width - width - ShadowLeft, -height - Gap - ShadowTop), PopupPrimaryAxis.Horizontal),
            new(new Point(target.Width + Gap - ShadowLeft, -ShadowTop), PopupPrimaryAxis.Vertical),
            new(new Point(-width - Gap - ShadowLeft, -ShadowTop), PopupPrimaryAxis.Vertical),
        ];
    }

    private void Next_Click(object sender, RoutedEventArgs e) => Acted?.Invoke(this, TourAction.Next);

    private void Back_Click(object sender, RoutedEventArgs e) => Acted?.Invoke(this, TourAction.Back);

    private void Skip_Click(object sender, RoutedEventArgs e) => Acted?.Invoke(this, TourAction.Skip);

    private void Close_Click(object sender, RoutedEventArgs e) => Acted?.Invoke(this, TourAction.Close);
}
