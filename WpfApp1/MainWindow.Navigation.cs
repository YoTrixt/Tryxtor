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
    static string Host(string url)
    {
        try
        {
            string host = new Uri(url).Host;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        }
        catch { return "New tab"; }
    }

    string Resolve(string input)
    {
        string value = input.Trim();

        if (value.Length == 0)
            return "https://www.google.com";

        // Handle local Windows file paths
        if (System.IO.File.Exists(value))
            return new Uri(value).AbsoluteUri;

        // Handle file:// URLs
        if (Uri.TryCreate(value, UriKind.Absolute, out var fileUri) &&
            fileUri.IsFile)
            return fileUri.AbsoluteUri;

        // Handle normal websites
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
            return uri.ToString();

        if (!value.Contains(' ') && value.Contains('.') &&
            Uri.TryCreate("https://" + value, UriKind.Absolute, out _))
            return "https://" + value;

        string search = SearchTemplate();
        return search.Replace("{query}", Uri.EscapeDataString(value));
    }

    string SearchTemplate() => preferences.SearchEngine switch
    {
        "Bing" => "https://www.bing.com/search?q={query}",
        "DuckDuckGo" => "https://duckduckgo.com/?q={query}",
        "Brave" => "https://search.brave.com/search?q={query}",
        _ => "https://www.google.com/search?q={query}"
    };

    void SearchEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchEngineBox.SelectedItem is not ComboBoxItem selected) return;
        preferences.SearchEngine = selected.Tag?.ToString() ?? "Google";
        SavePreferences();
    }

    void BuildSearchEngineIcons()
    {
        SearchEngineBox.Style = (Style)FindResource("SearchEnginePicker");
        SearchEngineBox.Width = 48;
        SearchEngineBox.Padding = new Thickness(0);
        SearchEngineBox.HorizontalContentAlignment = HorizontalAlignment.Center;
        SearchEngineBox.VerticalContentAlignment = VerticalAlignment.Center;
        SearchEngineBox.ToolTip = "Choose search engine";

        SearchEngineBox.Items.Clear();

        foreach (string engine in new[] { "Google", "Bing", "DuckDuckGo", "Brave" })
        {
            SearchEngineBox.Items.Add(new ComboBoxItem
            {
                Content = SearchEngineIcon(engine),
                Tag = engine,
                ToolTip = engine,
                Width = 44,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            });
        }
    }

    static FrameworkElement SearchEngineIcon(string engine)
    {
        // Use each provider's real favicon so the menu shows the actual brand mark.
        // The favicon endpoint returns a cached 64px PNG, suitable for the compact picker.
        string domain = engine switch
        {
            "Google" => "google.com",
            "Bing" => "bing.com",
            "DuckDuckGo" => "duckduckgo.com",
            "Brave" => "search.brave.com",
            _ => "google.com"
        };
        return new Image
        {
            Width = 18,
            Height = 18,
            ToolTip = engine,
            Stretch = Stretch.Uniform,
            Source = new BitmapImage(new Uri($"https://www.google.com/s2/favicons?domain={domain}&sz=64", UriKind.Absolute))
        };
    }

    void Navigate(string value, BrowserTab? tab = null)
    {
        SuggestionPopup.IsOpen = false;
        tab ??= activeTab;
        if (tab == null) return;
        string trimmed = value.Trim();
        if (!privateMode && trimmed.Length > 1 && !trimmed.Contains('.') && !trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            preferences.RecentSearches.RemoveAll(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase));
            preferences.RecentSearches.Insert(0, trimmed);
            if (preferences.RecentSearches.Count > 30) preferences.RecentSearches.RemoveRange(30, preferences.RecentSearches.Count - 30);
            SavePreferences();
        }
        var url = Resolve(value);
        tab.Url = url;
        SetAddressText(url);
        NewTabPanel.Visibility = Visibility.Collapsed;
        BrowserHost.Visibility = Visibility.Visible;
        tab.View.Source = new Uri(url);
    }

    void SetAddressText(string value)
    {
        settingAddressText = true;

        AddressBox.Text = AddressBox.IsKeyboardFocused
            ? value
            : FormatUrlForDisplay(value);

        settingAddressText = false;
    }

    static string FormatUrlForDisplay(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return value;

        string result = uri.Host;

        if (result.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            result = result[4..];

        result += uri.AbsolutePath.TrimEnd('/');

        if (!string.IsNullOrEmpty(uri.Query))
            result += uri.Query;

        if (!string.IsNullOrEmpty(uri.Fragment))
            result += uri.Fragment;

        return result;
    }

    void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (activeTab == null) return;

        SetAddressText(activeTab.Url);
        AddressBox.SelectAll();
    }

    void AddressBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (activeTab == null) return;

        SetAddressText(activeTab.Url);
    }

    void Back_Click(object sender, RoutedEventArgs e) { if (activeTab?.View.CoreWebView2?.CanGoBack == true) activeTab.View.CoreWebView2.GoBack(); }

    void Forward_Click(object sender, RoutedEventArgs e) { if (activeTab?.View.CoreWebView2?.CanGoForward == true) activeTab.View.CoreWebView2.GoForward(); }

    async void Reload_Click(object sender, RoutedEventArgs e)
    {
        var tab = activeTab;
        if (tab == null) return;
        if (string.IsNullOrWhiteSpace(tab.Url))
        {
            SuggestionPopup.IsOpen = false;
            SetAddressText("");
            HomeSearch.Clear();
            RenderShortcuts();
            RenderBookmarks();
            NewTabPanel.Visibility = Visibility.Visible;
            BrowserHost.Visibility = Visibility.Collapsed;
            return;
        }

        var core = tab.View.CoreWebView2;
        if (core == null) return;

        // Let the page write down its scroll position and typed input first, so it can put
        // them back after the reload. The short timeout keeps a frozen page from blocking Reload.
        try
        {
            var saved = core.ExecuteScriptAsync("window.__tryxtorSaveState && window.__tryxtorSaveState()");
            await Task.WhenAny(saved, Task.Delay(400));
        }
        catch (Exception) { }

        tab.View.CoreWebView2?.Reload();
    }

    void AddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!settingAddressText) UpdateSuggestions();
    }

    void UpdateSuggestions()
    {
        if (!preferences.ShowSuggestions || !AddressBox.IsKeyboardFocused)
        {
            SuggestionPopup.IsOpen = false;
            return;
        }
        string query = AddressBox.Text.Trim();
        if (query.Length < 2) { SuggestionPopup.IsOpen = false; return; }
        var results = new List<AddressSuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string title, string detail, string value, bool isSearch = false, bool isRecentSearch = false)
        {
            if (results.Count >= 8 || !seen.Add((isSearch ? "search:" : "link:") + value)) return;
            results.Add(new AddressSuggestion { Title = title, Detail = detail, Value = value, IsRecentSearch = isRecentSearch });
        }
        if (!privateMode)
            foreach (string recent in preferences.RecentSearches)
                if (recent.Contains(query, StringComparison.OrdinalIgnoreCase)) Add(recent, "Recent search", recent, true, true);
        foreach (var bookmark in preferences.Bookmarks)
            if (Matches(bookmark.Title, bookmark.Url, query)) Add(bookmark.Title, "Bookmark  ·  " + bookmark.Url, bookmark.Url);
        foreach (var shortcut in preferences.Shortcuts)
            if (Matches(shortcut.Title, shortcut.Url, query)) Add(shortcut.Title, "Shortcut  ·  " + shortcut.Url, shortcut.Url);
        if (!privateMode)
            foreach (var page in preferences.RecentPages)
                if (Matches(page.Title, page.Url, query)) Add(page.Title, "Recently visited  ·  " + page.Url, page.Url);
        Add("Search for “" + query + "”", "Search the web", query, true);
        currentSuggestions = results;
        SuggestionList.ItemsSource = currentSuggestions;
        SuggestionList.SelectedIndex = -1;
        SuggestionPopup.IsOpen = results.Count > 0;
    }

    static bool Matches(string title, string url, string query) =>
        title.Contains(query, StringComparison.OrdinalIgnoreCase) || url.Contains(query, StringComparison.OrdinalIgnoreCase);

    void SelectSuggestion(AddressSuggestion suggestion)
    {
        SuggestionPopup.IsOpen = false;
        Navigate(suggestion.Value);
        Keyboard.ClearFocus();
    }

    void SuggestionList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source != null)
        {
            if (source is Button) return;
            source = VisualTreeHelper.GetParent(source);
        }
        if (SuggestionList.SelectedItem is AddressSuggestion suggestion) SelectSuggestion(suggestion);
    }

    void RemoveRecentSearch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AddressSuggestion suggestion })
        {
            preferences.RecentSearches.RemoveAll(x => string.Equals(x, suggestion.Value, StringComparison.OrdinalIgnoreCase));
            SavePreferences();
            UpdateSuggestions();
        }
        e.Handled = true;
    }

    void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && SuggestionPopup.IsOpen)
        {
            e.Handled = true;
            SuggestionList.SelectedIndex = (SuggestionList.SelectedIndex + 1) % currentSuggestions.Count;
            SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
        }
        else if (e.Key == Key.Up && SuggestionPopup.IsOpen)
        {
            e.Handled = true;
            SuggestionList.SelectedIndex = SuggestionList.SelectedIndex <= 0 ? currentSuggestions.Count - 1 : SuggestionList.SelectedIndex - 1;
            SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (SuggestionPopup.IsOpen && SuggestionList.SelectedItem is AddressSuggestion suggestion) SelectSuggestion(suggestion);
            else { Navigate(AddressBox.Text); Keyboard.ClearFocus(); }
        }
        else if (e.Key == Key.Escape) SuggestionPopup.IsOpen = false;
    }

    void HomeSearch_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(HomeSearch.Text); }
}
