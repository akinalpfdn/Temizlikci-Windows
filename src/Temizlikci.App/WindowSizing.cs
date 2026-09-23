using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Temizlikci.App;

/// <summary>Default and minimum window size, in effective pixels scaled to the monitor's DPI.</summary>
internal static partial class WindowSizing
{
    private const int DefaultWidth = 1380;
    private const int DefaultHeight = 860;
    private const int MinimumWidth = 1100;
    private const int MinimumHeight = 640;
    private const double BaseDpi = 96;

    public static void Apply(Window window)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        double scale = GetDpiForWindow(handle) / BaseDpi;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(DefaultWidth * scale), (int)(DefaultHeight * scale)));
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(MinimumWidth * scale);
            presenter.PreferredMinimumHeight = (int)(MinimumHeight * scale);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);
}
