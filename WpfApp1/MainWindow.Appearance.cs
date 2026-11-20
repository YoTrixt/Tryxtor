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
    void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(1200, area.Width * .92);
        Height = Math.Min(760, area.Height * .90);
        MinWidth = Math.Min(480, area.Width);
        MinHeight = Math.Min(360, area.Height);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    void LoadPreferences()
    {
        try
        {
            if (System.IO.File.Exists(settingsPath))
                preferences = JsonSerializer.Deserialize<Preferences>(System.IO.File.ReadAllText(settingsPath)) ?? new();
        }
        catch { preferences = new(); }
        preferences.Bookmarks ??= new();
        preferences.Shortcuts ??= new();
        preferences.RecentSearches ??= new();
        preferences.RecentPages ??= new();
    }

    void SavePreferences()
    {
        if (privateMode) return;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(settingsPath)!);
            System.IO.File.WriteAllText(settingsPath, JsonSerializer.Serialize(preferences));
        }
        catch (Exception ex) { MessageBox.Show("Could not save settings: " + ex.Message, "Tryxtor"); }
    }

    void ApplyAppearance()
    {
        var theme = BuiltInThemes.TryGetValue(
            preferences.ColorTheme,
            out var selectedTheme)
            ? selectedTheme
            : BuiltInThemes["Midnight"];

        Color background = ParseColor(theme.Background);
        Color surface = ParseColor(theme.Surface);
        Color inset = ParseColor(theme.Inset);
        Color outline = ParseColor(theme.Outline);

        Brush text = ParseBrush(theme.Text, Brushes.White);
        Brush muted = ParseBrush(theme.Muted, Brushes.Gray);
        Brush accent = ParseBrush(preferences.Accent, Brushes.LightGreen);


        Resources["HoverSurface"] = new SolidColorBrush(
            Blend(surface, inset, .5));
        // Apply the selected theme to the browser chrome
        TitleBar.Background = new SolidColorBrush(surface);
        Toolbar.Background = new SolidColorBrush(surface);
        BookmarkBar.Background = new SolidColorBrush(surface);
        TabStrip.Background = new SolidColorBrush(surface);
        rootGrid.Background = new SolidColorBrush(background);
        if (!string.IsNullOrWhiteSpace(preferences.StartPageBackground) && System.IO.File.Exists(preferences.StartPageBackground))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(preferences.StartPageBackground); image.EndInit(); image.Freeze();
                NewTabPanel.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill, Opacity = .72 };
            }
            catch { NewTabPanel.Background = new SolidColorBrush(background); }
        }
        else NewTabPanel.Background = new SolidColorBrush(background);
        AddressSurface.Background = HomeSearchSurface.Background = new SolidColorBrush(inset);
        AddressSurface.BorderBrush = HomeSearchSurface.BorderBrush = new SolidColorBrush(outline);
        HomeSearchSurface.BorderThickness = AddressSurface.BorderThickness = new Thickness(1);
        HomeSearchMark.Foreground = accent;
        BrandLabel.Foreground = privateMode ? new SolidColorBrush(Color.FromRgb(190, 164, 255)) : accent;
        SearchEngineBox.Foreground = text;
        SearchEngineBox.Background = new SolidColorBrush(inset);
        SearchEngineBox.BorderBrush = new SolidColorBrush(outline);
        var engineItems = new Style(typeof(ComboBoxItem));
        engineItems.Setters.Add(new Setter(Control.ForegroundProperty, text));
        engineItems.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(surface)));
        SearchEngineBox.ItemContainerStyle = engineItems;
        HomeHeading.Foreground = AddressBox.Foreground = HomeSearch.Foreground = text;
        StarButton.Foreground = accent;
        HomeSubtitle.Foreground = QuickLinksLabel.Foreground = muted;
        BookmarkBar.Visibility = preferences.ShowBookmarks ? Visibility.Visible : Visibility.Collapsed;
        QuickLinks.Visibility = preferences.ShowShortcuts ? Visibility.Visible : Visibility.Collapsed;
        QuickLinksHeader.Visibility = preferences.ShowShortcuts ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in FindButtons(TitleBar)) button.Foreground = text;

        rootGrid.RowDefinitions[0].Height = new GridLength(preferences.Density == "Compact" ? 36 : preferences.Density == "Roomy" ? 50 : 42);
        rootGrid.RowDefinitions[1].Height = new GridLength(preferences.Density == "Compact" ? 42 : preferences.Density == "Roomy" ? 56 : 48);
        double pickerHeight = preferences.Density switch
        {
            "Compact" => 28,
            "Roomy" => 42,
            _ => 34
        };

        SearchEngineBox.Height = pickerHeight;
        SearchEngineBox.VerticalAlignment = VerticalAlignment.Center;

        foreach (ComboBoxItem item in SearchEngineBox.Items)
        {
            item.Height = pickerHeight - 2;

            if (item.Content is FrameworkElement icon)
            {
                double iconSize = pickerHeight - 8;
                icon.Width = iconSize;
                icon.Height = iconSize;
                icon.VerticalAlignment = VerticalAlignment.Center;
                icon.HorizontalAlignment = HorizontalAlignment.Center;
            }
        }
        StarButton.Padding = preferences.Density == "Compact" ? new Thickness(3, 0, 3, 0) : new Thickness(8, 4, 8, 4);
        foreach (var tab in tabs)
        {
            tab.TitleButton.Foreground = text;
            tab.Surface.Background = tab == activeTab
                ? new SolidColorBrush(Blend(ParseColor(preferences.Accent), background, .20))
                : new SolidColorBrush(Blend(surface, background, .5));
            tab.Surface.BorderBrush = tab == activeTab ? accent : Brushes.Transparent;
            tab.Surface.BorderThickness = new Thickness(0, 0, 0, tab == activeTab ? 2 : 0);
        }
        foreach (var button in Toolbar.Children.OfType<Panel>().SelectMany(p => p.Children.OfType<Button>())) button.Foreground = text;
        UpdateBookmarkButton();
        UpdateDownloadsIndicator();
    }

    static Brush ParseBrush(string value, Brush fallback)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(value)!; }
        catch { return fallback; }
    }

    static Color ParseColor(string value)
    {
        try { return (Color)ColorConverter.ConvertFromString(value)!; }
        catch { return Color.FromRgb(182, 243, 106); }
    }

    static Canvas CreateColorWheel()
    {
        const int size = 220;
        const double radius = 106;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = x - (size - 1) / 2.0;
                double dy = y - (size - 1) / 2.0;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                int offset = (y * size + x) * 4;
                if (distance > radius) continue;
                double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                Color color = HsvToColor(hue, Math.Min(1, distance / radius), 1);
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        var canvas = new Canvas { Width = size, Height = size, Background = Brushes.Transparent, IsHitTestVisible = true, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 5) };
        canvas.Children.Add(new Image { Source = bitmap, Width = size, Height = size, IsHitTestVisible = false });
        return canvas;
    }

    static Color ColorAtWheelPoint(Point point, double width, double height)
    {
        double centerX = width / 2, centerY = height / 2, radius = Math.Min(width, height) / 2 - 4;
        double dx = point.X - centerX, dy = point.Y - centerY;
        double distance = Math.Min(radius, Math.Sqrt(dx * dx + dy * dy));
        double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        return HsvToColor(hue, distance / radius, 1);
    }

    static Point ColorToWheelPoint(Color color, double width, double height)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        double hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        double saturation = max == 0 ? 0 : delta / max;
        double radius = (Math.Min(width, height) / 2 - 4) * saturation;
        double angle = hue * Math.PI / 180;
        return new Point(width / 2 + Math.Cos(angle) * radius, height / 2 + Math.Sin(angle) * radius);
    }

    static Color HsvToColor(double hue, double saturation, double value)
    {
        double chroma = value * saturation;
        double x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        double m = value - chroma;
        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    static Color Blend(Color a, Color backgroundColor, double amount) => Color.FromRgb(
        (byte)(a.R * amount + backgroundColor.R * (1 - amount)),
        (byte)(a.G * amount + backgroundColor.G * (1 - amount)),
        (byte)(a.B * amount + backgroundColor.B * (1 - amount)));

    static IEnumerable<Button> FindButtons(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button button) yield return button;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }
}
