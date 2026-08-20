using System.Globalization;
using System.Windows.Data;
using TCFModManagement.App.ViewModels;
using Wpf.Ui.Controls;

namespace TCFModManagement.App.Converters;

// Colors a DownloadsPage queue card's status ui:Badge by DownloadQueueItemViewModel.Status.
public sealed class DownloadQueueStatusToAppearanceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DownloadQueueItemStatus status
            ? status switch
            {
                DownloadQueueItemStatus.Pending => ControlAppearance.Secondary,
                DownloadQueueItemStatus.Downloading => ControlAppearance.Info,
                DownloadQueueItemStatus.Installing => ControlAppearance.Caution,
                DownloadQueueItemStatus.Completed => ControlAppearance.Success,
                DownloadQueueItemStatus.Failed => ControlAppearance.Danger,
                DownloadQueueItemStatus.Cancelled => ControlAppearance.Secondary,
                _ => ControlAppearance.Secondary,
            }
            : ControlAppearance.Secondary;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
