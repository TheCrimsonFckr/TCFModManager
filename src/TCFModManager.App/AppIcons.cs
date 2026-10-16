using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TCFModManager.Core.Services;
using Wpf.Ui.Appearance;

namespace TCFModManager.App;

//
// The window's taskbar and Alt-Tab icon and the tray icon follow Windows' theme, the title bar icon
// follows the app's: black on light, white on anything else. The exe's own icon (Explorer, shortcuts,
// a pinned taskbar button) is fixed at build time and stays the colour one.
//
internal static class AppIcons
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri Black = new("pack://application:,,,/Assets/AppIconBlack.ico");
    private static readonly Uri White = new("pack://application:,,,/Assets/AppIconWhite.ico");

    private static readonly Dictionary<Uri, BitmapDecoder> Decoders = [];

    private static bool _watching;

    public static event EventHandler? Changed;

    public static bool WindowsIsLight
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                AppLog.Debug("Theme", $"couldn't read the Windows theme: {ex.Message}");
                return false;
            }
        }
    }

    public static ImageSource ForWindow => Decoder(WindowsIsLight ? Black : White).Frames[0];

    public static ImageSource ForTray => Frame(WindowsIsLight ? Black : White, 32);

    public static ImageSource ForTitleBar => Frame(ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Light ? Black : White, 32);

    public static void Watch()
    {
        if (_watching) return;
        _watching = true;

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color) Raise();
        };
        ApplicationThemeManager.Changed += (_, _) => Raise();
    }

    private static void Raise()
    {
        if (Application.Current?.Dispatcher is not { } dispatcher) return;
        dispatcher.BeginInvoke(() => Changed?.Invoke(null, EventArgs.Empty));
    }

    private static BitmapSource Frame(Uri icon, int size) =>
        Decoder(icon).Frames.OrderBy(f => Math.Abs(f.PixelWidth - size)).First();

    private static BitmapDecoder Decoder(Uri icon)
    {
        if (!Decoders.TryGetValue(icon, out var decoder))
        {
            decoder = BitmapDecoder.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Decoders[icon] = decoder;
        }

        return decoder;
    }
}
