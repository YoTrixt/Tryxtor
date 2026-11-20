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
    const int WmGetMinMaxInfo = 0x0024;

    const int WmNcHitTest = 0x0084;

    const int WmLButtonDown = 0x0201;

    const int WmLButtonUp = 0x0202;

    const int HtClient = 1;

    const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeMinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    private bool closeCornerPressed;

    IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (WindowState == WindowState.Maximized)
        {
            if (message == WmNcHitTest)
            {
                // Keep the top-right screen corner in our client area. Returning
                // HTCLOSE here would make Windows draw its own caption button.
                int screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                var monitorInfo = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };

                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
                {
                    var dpi = VisualTreeHelper.GetDpi(this);
                    int closeWidth = (int)Math.Ceiling(46 * dpi.DpiScaleX);
                    int closeHeight = (int)Math.Ceiling(32 * dpi.DpiScaleY);
                    bool inTopRightCorner =
                        screenX >= monitorInfo.Monitor.Right - closeWidth &&
                        screenX < monitorInfo.Monitor.Right &&
                        screenY >= monitorInfo.Monitor.Top &&
                        screenY < monitorInfo.Monitor.Top + closeHeight;

                    if (inTopRightCorner)
                    {
                        handled = true;
                        return new IntPtr(HtClient);
                    }
                }
            }
            else if (message == WmLButtonDown)
            {
                int x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                if (IsInCloseCorner(hwnd, x, y))
                {
                    closeCornerPressed = true;
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            else if (message == WmLButtonUp && closeCornerPressed)
            {
                closeCornerPressed = false;
                int x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                handled = true;
                if (IsInCloseCorner(hwnd, x, y))
                    Close_Click(this, new RoutedEventArgs());
                return IntPtr.Zero;
            }
        }
        else
        {
            closeCornerPressed = false;
        }

        if (message != WmGetMinMaxInfo) return IntPtr.Zero;
        IntPtr workAreaMonitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var workAreaInfo = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (workAreaMonitor == IntPtr.Zero || !GetMonitorInfo(workAreaMonitor, ref workAreaInfo)) return IntPtr.Zero;

        var limits = Marshal.PtrToStructure<NativeMinMaxInfo>(lParam);
        limits.MaxPosition.X = workAreaInfo.Work.Left - workAreaInfo.Monitor.Left;
        limits.MaxPosition.Y = workAreaInfo.Work.Top - workAreaInfo.Monitor.Top;
        limits.MaxSize.X = workAreaInfo.Work.Right - workAreaInfo.Work.Left;
        limits.MaxSize.Y = workAreaInfo.Work.Bottom - workAreaInfo.Work.Top;
        var dpiForLimits = VisualTreeHelper.GetDpi(this);
        limits.MinTrackSize.X = (int)Math.Ceiling(MinWidth * dpiForLimits.DpiScaleX);
        limits.MinTrackSize.Y = (int)Math.Ceiling(MinHeight * dpiForLimits.DpiScaleY);
        Marshal.StructureToPtr(limits, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    bool IsInCloseCorner(IntPtr hwnd, int x, int y)
    {
        if (!GetClientRect(hwnd, out var clientRect)) return false;
        var dpi = VisualTreeHelper.GetDpi(this);
        int closeWidth = (int)Math.Ceiling(46 * dpi.DpiScaleX);
        int closeHeight = (int)Math.Ceiling(32 * dpi.DpiScaleY);
        return x >= clientRect.Right - closeWidth && x < clientRect.Right &&
               y >= clientRect.Top && y < clientRect.Top + closeHeight;
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current != null)
        {
            if (current is Button) return;
            current = VisualTreeHelper.GetParent(current);
        }
        if (e.ClickCount == 2) { Maximize_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
