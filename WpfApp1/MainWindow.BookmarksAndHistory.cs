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
    void RenderBookmarks()
    {
        BookmarkBar.Children.Clear();
        BookmarkBar.Visibility = preferences.ShowBookmarks ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in preferences.Bookmarks)
        {
            var button = new Button { Content = item.Title, Style = (Style)FindResource("ChromeButton"), FontSize = 11, Foreground = ParseBrush(preferences.Theme == "Light" ? "#343943" : "#C9CCD4", Brushes.White), ToolTip = item.Url };
            button.Click += (_, _) => Navigate(item.Url);
            button.ContextMenu = new ContextMenu();
            var remove = new MenuItem { Header = "Remove bookmark" };
            remove.Click += (_, _) => { preferences.Bookmarks.Remove(item); SavePreferences(); RenderBookmarks(); };
            button.ContextMenu.Items.Add(remove);
            BookmarkBar.Children.Add(button);
        }
    }

    void RenderShortcuts()
    {
        QuickLinks.Children.Clear();
        foreach (var item in preferences.Shortcuts)
        {
            var button = new Button { Content = item.Title, Style = (Style)FindResource("ChromeButton"), MinWidth = 84, MinHeight = 54, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(preferences.Theme == "Light" ? Color.FromRgb(230, 233, 238) : Color.FromRgb(27, 31, 39)), Foreground = ParseBrush(preferences.Theme == "Light" ? "#343943" : "#E5E8ED", Brushes.White), ToolTip = item.Url };
            button.Click += (_, _) => Navigate(item.Url);
            var remove = new MenuItem { Header = "Remove shortcut" };
            remove.Click += (_, _) => { preferences.Shortcuts.Remove(item); SavePreferences(); RenderShortcuts(); };
            button.ContextMenu = new ContextMenu();
            button.ContextMenu.Items.Add(remove);
            QuickLinks.Children.Add(button);
        }
    }

    void AddShortcut_Click(object sender, RoutedEventArgs e)
    {
        string? title = Ask("Shortcut name", "Add a shortcut");
        if (string.IsNullOrWhiteSpace(title)) return;
        string? url = Ask("Website address", "Add a shortcut");
        if (string.IsNullOrWhiteSpace(url)) return;
        preferences.Shortcuts.Add(new Shortcut { Title = title.Trim(), Url = Resolve(url) });
        SavePreferences();
        RenderShortcuts();
    }

    string? Ask(string prompt, string title)
    {
        var dialog = new Window { Title = title, Owner = this, Width = 390, Height = 170, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(25, 28, 36)), Foreground = Brushes.White, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { Padding = new Thickness(8) };
        panel.Children.Add(input);
        var submit = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        submit.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(submit);
        dialog.Content = panel;
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        if (activeTab == null || string.IsNullOrEmpty(activeTab.Url)) return;
        var existing = preferences.Bookmarks.FirstOrDefault(x => x.Url == activeTab.Url);
        if (existing != null) preferences.Bookmarks.Remove(existing);
        else preferences.Bookmarks.Add(new Bookmark { Title = activeTab.Title, Url = activeTab.Url });
        SavePreferences();
        RenderBookmarks();
        UpdateBookmarkButton();
    }

    void UpdateBookmarkButton()
    {
        bool saved = activeTab != null && preferences.Bookmarks.Any(x => x.Url == activeTab.Url);
        StarButton.Foreground = ParseBrush(saved ? preferences.Accent : (preferences.Theme == "Light" ? "#586475" : "#C9CCD4"), Brushes.White);
        StarButton.ToolTip = saved ? "Remove bookmark" : "Bookmark current page";
    }

    void History_Click(object sender, RoutedEventArgs e)
    {
        if (privateMode)
        {
            MessageBox.Show("History is not available in this incognito window.", "Tryxtor Incognito", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new Window { Title = "Browsing history", Owner = this, Width = 600, Height = 680, MinWidth = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(17, 20, 27)), Foreground = new SolidColorBrush(Color.FromRgb(239, 242, 247)) };
        var layout = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = "History", FontSize = 23, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Recent pages and searches are stored on this PC.", Foreground = new SolidColorBrush(Color.FromRgb(161, 170, 183)), FontSize = 11, Margin = new Thickness(0, 3, 0, 10) });
        var filter = new TextBox { Padding = new Thickness(9), Background = new SolidColorBrush(Color.FromRgb(27, 32, 41)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(52, 60, 72)) };
        header.Children.Add(filter);
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var entries = new StackPanel();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var clear = new Button { Content = "Clear pages and searches", Padding = new Thickness(12, 8, 12, 8) };
        clear.Click += (_, _) => { preferences.RecentPages.Clear(); preferences.RecentSearches.Clear(); SavePreferences(); Render(); };
        var close = new Button { Content = "Done", Padding = new Thickness(15, 8, 15, 8), Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => dialog.Close();
        footer.Children.Add(clear); footer.Children.Add(close); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var scroll = new ScrollViewer { Content = entries, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        layout.Children.Add(scroll);

        void AddRow(string title, string detail, Action open, Action remove)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), Background = new SolidColorBrush(Color.FromRgb(27, 32, 41)) };
            var removeButton = new Button { Content = "x", ToolTip = "Remove", Padding = new Thickness(10, 6, 10, 6), Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(190, 199, 211)), BorderThickness = new Thickness(0) };
            removeButton.Click += (_, _) => { remove(); SavePreferences(); Render(); };
            DockPanel.SetDock(removeButton, Dock.Right); row.Children.Add(removeButton);
            var openButton = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 7, 10, 7), Content = new StackPanel { Children = { new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 12 }, new TextBlock { Text = detail, Foreground = new SolidColorBrush(Color.FromRgb(161, 170, 183)), FontSize = 10, Margin = new Thickness(0, 2, 0, 0) } } } };
            openButton.Click += (_, _) => { open(); dialog.Close(); };
            row.Children.Add(openButton); entries.Children.Add(new Border { Child = row, CornerRadius = new CornerRadius(8) });
        }

        void Render()
        {
            entries.Children.Clear();
            string query = filter.Text.Trim();
            entries.Children.Add(new TextBlock { Text = "RECENT PAGES", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(161, 170, 183)), Margin = new Thickness(2, 5, 0, 5) });
            foreach (var page in preferences.RecentPages.ToList().Where(x => query.Length == 0 || Matches(x.Title, x.Url, query)))
                AddRow(page.Title, page.Url, () => { if (activeTab != null) Navigate(page.Url); }, () => preferences.RecentPages.Remove(page));
            entries.Children.Add(new TextBlock { Text = "RECENT SEARCHES", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(161, 170, 183)), Margin = new Thickness(2, 14, 0, 5) });
            foreach (var term in preferences.RecentSearches.ToList().Where(x => query.Length == 0 || x.Contains(query, StringComparison.OrdinalIgnoreCase)))
                AddRow(term, "Search phrase", () => { if (activeTab != null) Navigate(term); }, () => preferences.RecentSearches.RemoveAll(x => x == term));
        }
        filter.TextChanged += (_, _) => Render();
        Render();
        dialog.Content = layout;
        dialog.ShowDialog();
    }
}
