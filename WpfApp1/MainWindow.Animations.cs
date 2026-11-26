using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Interop;
using WpfApp1.PasswordManager;
using System.Windows.Controls.Primitives;

namespace WpfApp1;

public partial class MainWindow
{
    // ---------------------------------------------------------------------
    // Themed control styles
    // ---------------------------------------------------------------------

    static bool themeDictionaryLoaded;

    // Merges ModernControls.xaml into the application so every window and dialog
    // (Settings, Downloads, History, popups...) gets the themed controls.
    static void EnsureThemeDictionary()
    {
        if (themeDictionaryLoaded || Application.Current == null) return;
        themeDictionaryLoaded = true;
        string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!;
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{assemblyName};component/ModernControls.xaml", UriKind.Absolute)
        });
    }

    static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // Publishes the current theme to the DynamicResource brushes used by ModernControls.xaml.
    void UpdateThemeResources(BrowserTheme theme)
    {
        EnsureThemeDictionary();
        if (Application.Current == null) return;

        var resources = Application.Current.Resources;
        Color accent = ParseColor(preferences.Accent);
        bool light = preferences.Theme == "Light";
        double luminance = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255.0;

        resources["ThemeAccent"] = FrozenBrush(accent);
        resources["ThemeOnAccent"] = FrozenBrush(luminance > 0.55 ? Color.FromRgb(15, 17, 21) : Colors.White);
        resources["ThemeInk"] = FrozenBrush(ParseColor(theme.Text));
        resources["ThemeMuted"] = FrozenBrush(ParseColor(theme.Muted));
        resources["ThemePopup"] = FrozenBrush(ParseColor(theme.Surface));
        resources["ThemeOutline"] = FrozenBrush(ParseColor(theme.Outline));
        resources["ThemeHoverOverlay"] = FrozenBrush(light ? Color.FromArgb(0x16, 0, 0, 0) : Color.FromArgb(0x1C, 255, 255, 255));
        resources["ThemePressOverlay"] = FrozenBrush(light ? Color.FromArgb(0x26, 0, 0, 0) : Color.FromArgb(0x30, 255, 255, 255));
        resources["ThemeSelection"] = FrozenBrush(Color.FromArgb(0x38, accent.R, accent.G, accent.B));
    }

    // ---------------------------------------------------------------------
    // Page loading animation (toolbar sweep, spinning reload icon, pulsing tab title)
    // ---------------------------------------------------------------------

    // Respects the Windows "Show animations in Windows" setting.
    static bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;

    bool loadingShown;

    void SetTabLoading(BrowserTab tab, bool loading)
    {
        tab.IsLoading = loading;

        if (AnimationsEnabled)
        {
            if (loading)
            {
                tab.TitleButton.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(700))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            }
            else
            {
                var fadeBack = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180));
                fadeBack.Completed += (_, _) =>
                {
                    if (tab.IsLoading) return;
                    tab.TitleButton.BeginAnimation(UIElement.OpacityProperty, null);
                    tab.TitleButton.Opacity = 1;
                };
                tab.TitleButton.BeginAnimation(UIElement.OpacityProperty, fadeBack);
            }
        }

        if (tab == activeTab) UpdateLoadingIndicator();
    }

    void UpdateLoadingIndicator()
    {
        bool loading = activeTab != null && activeTab.IsLoading && !string.IsNullOrWhiteSpace(activeTab.Url);
        if (loading == loadingShown) return;
        loadingShown = loading;

        if (loading) StartLoadingAnimation();
        else StopLoadingAnimation();
    }

    void StartLoadingAnimation()
    {
        if (!AnimationsEnabled) return;

        double travel = Math.Max(LoadingStrip.ActualWidth, 400);
        LoadingStrip.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        LoadingBarShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-LoadingBar.Width, travel, TimeSpan.FromMilliseconds(1100))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        });
        ReloadRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
        {
            RepeatBehavior = RepeatBehavior.Forever
        });
    }

    void StopLoadingAnimation()
    {
        // Finish the current spin of the reload icon instead of snapping back.
        double angle = ReloadRotation.Angle % 360;
        ReloadRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        ReloadRotation.Angle = 0;
        if (AnimationsEnabled && angle > 1)
        {
            ReloadRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, 360, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) =>
        {
            if (loadingShown) return;
            LoadingBarShift.BeginAnimation(TranslateTransform.XProperty, null);
            LoadingBarShift.X = -LoadingBar.Width;
            LoadingStrip.BeginAnimation(UIElement.OpacityProperty, null);
            LoadingStrip.Opacity = 0;
        };
        LoadingStrip.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // ---------------------------------------------------------------------
    // Download animation (bouncing arrow, progress line, completion pop)
    // ---------------------------------------------------------------------

    const double DownloadsProgressMaxWidth = 12;
    bool downloadsAnimating;
    bool downloadsIndeterminate;
    bool downloadsBaselineSet;
    DateTime downloadsFlashUntil = DateTime.MinValue;
    System.Windows.Threading.DispatcherTimer? downloadsFlashTimer;
    readonly HashSet<DownloadItem> downloadsCompletionSeen = new();

    void UpdateDownloadsIndicator()
    {
        var visible = downloadItems.Where(d => !d.Incognito || privateMode).ToList();
        var active = visible.Where(d => d.Status == DownloadStatus.Downloading).ToList();
        bool anyActive = active.Count > 0;

        // A download that finished since the last update gets a short celebration.
        bool justCompleted = false;
        foreach (var item in visible.Where(d => d.Status == DownloadStatus.Completed))
        {
            if (downloadsCompletionSeen.Add(item) && downloadsBaselineSet && !item.Restored)
                justCompleted = true;
        }
        downloadsBaselineSet = true;

        var theme = BuiltInThemes.TryGetValue(preferences.ColorTheme, out var selected) ? selected : BuiltInThemes["Midnight"];
        bool flashing = DateTime.UtcNow < downloadsFlashUntil;
        DownloadsButton.Foreground = anyActive || flashing
            ? ParseBrush(preferences.Accent, Brushes.LightGreen)
            : ParseBrush(theme.Text, Brushes.White);

        long received = active.Where(d => d.TotalBytes > 0).Sum(d => d.BytesReceived);
        long total = active.Where(d => d.TotalBytes > 0).Sum(d => d.TotalBytes ?? 0);
        double fraction = total > 0 ? Math.Clamp((double)received / total, 0, 1) : 0;
        SetDownloadsAnimation(anyActive, fraction, indeterminate: anyActive && total == 0);

        if (justCompleted) PlayDownloadCompleteAnimation();
    }

    void SetDownloadsAnimation(bool active, double fraction, bool indeterminate)
    {
        if (!AnimationsEnabled)
        {
            DownloadsProgress.Opacity = active ? 1 : 0;
            DownloadsProgress.Width = active ? Math.Max(2, fraction * DownloadsProgressMaxWidth) : 0;
            return;
        }

        if (active && !downloadsAnimating)
        {
            downloadsAnimating = true;
            DownloadsArrowShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 2.5, TimeSpan.FromMilliseconds(520))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
            DownloadsProgress.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
        }
        else if (!active && downloadsAnimating)
        {
            downloadsAnimating = false;
            downloadsIndeterminate = false;
            DownloadsArrowShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
            fade.Completed += (_, _) =>
            {
                if (downloadsAnimating) return;
                DownloadsProgress.BeginAnimation(FrameworkElement.WidthProperty, null);
                DownloadsProgress.Width = 0;
            };
            DownloadsProgress.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        if (!active) return;

        if (indeterminate)
        {
            // Size unknown: breathe the progress line instead of filling it.
            if (!downloadsIndeterminate)
            {
                downloadsIndeterminate = true;
                DownloadsProgress.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(3, DownloadsProgressMaxWidth, TimeSpan.FromMilliseconds(800))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            }
            return;
        }

        downloadsIndeterminate = false;
        DownloadsProgress.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(Math.Max(2, fraction * DownloadsProgressMaxWidth), TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    void PlayDownloadCompleteAnimation()
    {
        // Keep the icon in the accent color for a moment after a download finishes.
        downloadsFlashUntil = DateTime.UtcNow.AddSeconds(1.6);
        downloadsFlashTimer ??= new System.Windows.Threading.DispatcherTimer();
        downloadsFlashTimer.Stop();
        downloadsFlashTimer.Interval = TimeSpan.FromSeconds(1.7);
        downloadsFlashTimer.Tick -= DownloadsFlashTimer_Tick;
        downloadsFlashTimer.Tick += DownloadsFlashTimer_Tick;
        downloadsFlashTimer.Start();
        DownloadsButton.Foreground = ParseBrush(preferences.Accent, Brushes.LightGreen);

        if (!AnimationsEnabled) return;

        DoubleAnimationUsingKeyFrames Pop() => new()
        {
            KeyFrames =
            {
                new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new EasingDoubleKeyFrame(1.35, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130)), new CubicEase { EasingMode = EasingMode.EaseOut }),
                new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 })
            }
        };
        DownloadsPop.BeginAnimation(ScaleTransform.ScaleXProperty, Pop());
        DownloadsPop.BeginAnimation(ScaleTransform.ScaleYProperty, Pop());
    }

    void DownloadsFlashTimer_Tick(object? sender, EventArgs e)
    {
        downloadsFlashTimer?.Stop();
        UpdateDownloadsIndicator();
    }
}
