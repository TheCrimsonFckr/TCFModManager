using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.Core.SpModApi;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class DownloadsViewModel : ObservableObject
{
    private readonly SpModApiClient _spModApi;
    private readonly ModDownloadService _downloadService;

    public DownloadsViewModel() : this(AppServices.SpModApi, AppServices.Downloads)
    {
    }

    public DownloadsViewModel(SpModApiClient spModApi, ModDownloadService downloadService)
    {
        _spModApi = spModApi;
        _downloadService = downloadService;
    }

    [ObservableProperty]
    private string _modId = string.Empty;

    [ObservableProperty]
    private string _version = string.Empty;

    // Defaults to AppPaths.StagingDirectory, a "Staging" folder next to the exe.
    [ObservableProperty]
    private string _destinationFolder = AppPaths.StagingDirectory;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(ModId) || string.IsNullOrWhiteSpace(Version))
        {
            StatusMessage = "Enter a mod id (or GUID) and an exact version.";
            return;
        }

        IsBusy = true;
        Progress = 0;
        StatusMessage = null;
        try
        {
            var versions = await _spModApi.GetModVersionsAsync(
                ModId.Trim(),
                new ModVersionsQuery { FilterVersion = Version.Trim(), PerPage = 5 });

            var match = versions.Data.FirstOrDefault(v => v.Version == Version.Trim()) ?? versions.Data.FirstOrDefault();
            if (match?.Link is null)
            {
                StatusMessage = "Couldn't find that mod version on sp-mod.com.";
                return;
            }

            var fileName = $"{ModId.Trim()}-{match.Version}.zip";
            var destination = Path.Combine(DestinationFolder, fileName);
            var progress = new Progress<double>(p => Progress = p);

            await _downloadService.DownloadAsync(match.Link, destination, progress);

            StatusMessage = $"Downloaded to {destination}";
        }
        catch (SpModApiRateLimitedException ex)
        {
            StatusMessage = $"Rate limited by sp-mod.com - try again in {ex.RetryAfter?.TotalSeconds ?? 30:N0}s.";
        }
        catch (SpModApiException ex)
        {
            StatusMessage = $"sp-mod.com error: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
        }
        catch (IOException ex)
        {
            StatusMessage = $"Couldn't write the file: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
