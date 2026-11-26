using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Interop;
using WpfApp1.PasswordManager;
using System.Windows.Controls.Primitives;

namespace WpfApp1;

public partial class MainWindow
{
    // True while a page (for example a YouTube video) has an element in full screen.
    bool isFullScreen;
    WindowState stateBeforeFullScreen = WindowState.Normal;
    Rect boundsBeforeFullScreen;
    NativeRect fullScreenRect;
    GridLength titleRowBeforeFullScreen = new(42);
    GridLength toolbarRowBeforeFullScreen = new(48);

    // F11 browser full screen: stays on while you switch tabs, unlike a page's own full screen.
    bool browserFullScreen;

    // Makes the window match the situation: full screen if F11 was pressed or the active tab's page
    // (for example a video) is in full screen, otherwise normal. Full screen hides all browser
    // chrome and covers the whole monitor, taskbar included.
    void SyncFullScreen()
    {
        bool pageFullScreen = activeTab?.View.CoreWebView2?.ContainsFullScreenElement == true;
        SetFullScreen(browserFullScreen || pageFullScreen);
    }

    // F11. If a page is showing a full screen video, F11 closes it, like in other browsers.
    void ToggleBrowserFullScreen()
    {
        var core = activeTab?.View.CoreWebView2;
        if (core != null && core.ContainsFullScreenElement)
        {
            browserFullScreen = false;
            _ = core.ExecuteScriptAsync("document.fullscreenElement && document.exitFullscreen()");
            return;   // the page reports the change and SyncFullScreen finishes the job
        }

        browserFullScreen = !browserFullScreen;
        SyncFullScreen();
    }

    // Pages can't see a real key press happen, but they can post any message to the browser, so
    // the F11 message carries a secret that only the injected script below knows. That stops a
    // website from forcing the browser into full screen (and hiding the address bar) on its own.
    static readonly string fullScreenNonce = Guid.NewGuid().ToString("N");

    static string FullScreenKeyScript => """
        (() => {
            try {
                window.addEventListener('keydown', (e) => {
                    if (!e.isTrusted || e.key !== 'F11' || e.repeat) return;
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    window.chrome.webview.postMessage('trixter:toggle-fullscreen:__NONCE__');
                }, true);
            } catch (_) { }
        })();
        """.Replace("__NONCE__", fullScreenNonce);

    void SetFullScreen(bool on)
    {
        if (on == isFullScreen) return;
        isFullScreen = on;

        if (on)
        {
            // Find the monitor first (while the window is still where the user left it).
            if (!TryGetMonitorRect(out fullScreenRect))
            {
                isFullScreen = false;
                return;
            }
            stateBeforeFullScreen = WindowState;
            titleRowBeforeFullScreen = rootGrid.RowDefinitions[0].Height;
            toolbarRowBeforeFullScreen = rootGrid.RowDefinitions[1].Height;
            SuggestionPopup.IsOpen = false;

            rootGrid.RowDefinitions[0].Height = new GridLength(0);
            rootGrid.RowDefinitions[1].Height = new GridLength(0);
            TitleBar.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;
            LoadingStrip.Visibility = Visibility.Collapsed;
            BookmarkBar.Visibility = Visibility.Collapsed;

            // A maximized window is always limited to the work area (that is what keeps your normal
            // window above the taskbar), so full screen does not use maximize. It places a normal
            // window exactly over the whole monitor and makes it topmost instead.
            boundsBeforeFullScreen = RestoreBounds;
            WindowState = WindowState.Normal;
            Topmost = true;
            CoverMonitor();
            MarkFullScreenForShell(true);
            Activated += FullScreenWindowActivated;
            Deactivated += FullScreenWindowDeactivated;
        }
        else
        {
            Activated -= FullScreenWindowActivated;
            Deactivated -= FullScreenWindowDeactivated;
            Topmost = false;
            MarkFullScreenForShell(false);

            rootGrid.RowDefinitions[0].Height = titleRowBeforeFullScreen;
            rootGrid.RowDefinitions[1].Height = toolbarRowBeforeFullScreen;
            TitleBar.Visibility = Visibility.Visible;
            Toolbar.Visibility = Visibility.Visible;
            LoadingStrip.Visibility = Visibility.Visible;
            BookmarkBar.Visibility = preferences.ShowBookmarks ? Visibility.Visible : Visibility.Collapsed;

            WindowState = WindowState.Normal;
            Left = boundsBeforeFullScreen.Left;
            Top = boundsBeforeFullScreen.Top;
            Width = boundsBeforeFullScreen.Width;
            Height = boundsBeforeFullScreen.Height;
            if (stateBeforeFullScreen == WindowState.Maximized) WindowState = WindowState.Maximized;
        }

        UpdateResizeBorder();
    }

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    static readonly IntPtr HwndTopmost = new(-1);
    const uint SwpNoActivate = 0x0010;
    const uint SwpShowWindow = 0x0040;

    // The monitor the window is currently on, in physical pixels.
    bool TryGetMonitorRect(out NativeRect rect)
    {
        rect = default;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return false;
        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
        rect = info.Monitor;
        return true;
    }

    void CoverMonitor()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HwndTopmost,
            fullScreenRect.Left, fullScreenRect.Top,
            fullScreenRect.Right - fullScreenRect.Left, fullScreenRect.Bottom - fullScreenRect.Top,
            SwpNoActivate | SwpShowWindow);
    }

    // Don't stay on top of other apps when you alt-tab away from a full screen video.
    void FullScreenWindowActivated(object? sender, EventArgs e)
    {
        if (!isFullScreen) return;
        Topmost = true;
        CoverMonitor();
        MarkFullScreenForShell(true);
    }

    void FullScreenWindowDeactivated(object? sender, EventArgs e)
    {
        if (!isFullScreen) return;
        Topmost = false;
        MarkFullScreenForShell(false);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    class TaskbarListComObject { }

    [ComImport, Guid("602D4995-B13A-429b-A66E-1935E44F4317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITaskbarList2
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullScreen);
    }

    void MarkFullScreenForShell(bool fullScreen)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var taskbar = (ITaskbarList2)new TaskbarListComObject();
            taskbar.HrInit();
            taskbar.MarkFullscreenWindow(hwnd, fullScreen);
            Marshal.ReleaseComObject(taskbar);
        }
        catch (Exception)
        {
            // Topmost alone still covers the taskbar in most cases.
        }
    }

    // No resize edges while full screen, otherwise the screen borders steal mouse clicks from the page.
    void UpdateResizeBorder()
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome == null) return;
        chrome.ResizeBorderThickness = isFullScreen
            ? new Thickness(0)
            : WindowState == WindowState.Maximized ? new Thickness(10, 0, 10, 10) : new Thickness(10);
    }
}