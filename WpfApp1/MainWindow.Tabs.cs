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
    void CreateTabHeader(BrowserTab tab)
    {
        tab.Surface = new Border
        {
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(32, 35, 43)),
            Margin = new Thickness(2, TabTopGap, 2, 0),
            BorderThickness = new Thickness(0, 0, 0, 2)
        };

        var header = new Grid();

        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        var close = new Button
        {
            Content = "x",
            ToolTip = "Close tab",
            Style = (Style)FindResource("TabTitleButton"),
            Width = 24,
            MinWidth = 0,
            Padding = new Thickness(0),
            FontSize = 11
        };
        close.Click += (_, e) => { e.Handled = true; CloseTab(tab); };


        tab.TitleButton = new Button
        {
            Content = tab.Title,
            ToolTip = tab.Title,
            Style = (Style)FindResource("TabTitleButton"),
            Background = Brushes.Transparent,
            Foreground = ParseBrush(preferences.Theme == "Light" ? "#263142" : "#E7EAF0", Brushes.White),
            MinWidth = 0,
            MaxWidth = double.PositiveInfinity,
            Padding = new Thickness(8, 7, 3, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        tab.TitleButton.Click += (_, e) => { e.Handled = true; ActivateTab(tab); };
        tab.TitleButton.PreviewMouseLeftButtonDown += (_, e) =>
        {
            tabDragSource = tab;
            tabDragStart = tab.TitleButton.PointToScreen(e.GetPosition(tab.TitleButton));
            tabDragStarted = false;
        };

        Grid.SetColumn(tab.TitleButton, 0);
        Grid.SetColumn(close, 1);

        header.Children.Add(tab.TitleButton);
        header.Children.Add(close);

        tab.Surface.Child = header;
    }

    // Tabs float 5px below the top edge, except when maximized: then they reach the very top of the
    // screen so you can throw the mouse up there and still hit a tab (Fitts's law).
    double TabTopGap => WindowState == WindowState.Maximized ? 0 : 5;

    void UpdateTabTopGap()
    {
        foreach (var tab in tabs) tab.Surface.Margin = new Thickness(2, TabTopGap, 2, 0);
    }

    void UpdateTabStripLayout()
    {
        TabStrip.Rows = 1;
        TabStrip.Columns = Math.Max(1, tabs.Count);
    }

    async Task<bool> AddTab(string? address = null)
    {
        // Close any password popup before creating a new tab.
        if (passwordAccountPopup?.IsOpen == true)
        {
            passwordAccountPopup.IsOpen = false;
            passwordAccountPopup = null;
        }
        var tab = new BrowserTab { Owner = this };
        CreateTabHeader(tab);
        tabs.Add(tab);
        TabStrip.Children.Add(tab.Surface);
        UpdateTabStripLayout();

        tab.View.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 16, 19, 25);
        BrowserHost.Children.Add(tab.View);
        tab.View.NavigationStarting += (_, e) =>
        {
            if (tab.Owner.passwordAccountPopup?.IsOpen == true &&
                tab.Owner.passwordAccountPopup.PlacementTarget == tab.View)
            {
                tab.Owner.passwordAccountPopup.IsOpen = false;
                tab.Owner.passwordAccountPopup = null;
            }
            tab.Url = e.Uri;
            tab.Owner.SetTabLoading(tab, true);
            if (tab == tab.Owner.activeTab) tab.Owner.SetAddressText(e.Uri);
        };
        tab.View.NavigationCompleted += (_, _) =>
        {
            tab.Owner.SetTabLoading(tab, false);
            string? documentTitle = tab.View.CoreWebView2?.DocumentTitle;
            tab.Title = string.IsNullOrWhiteSpace(documentTitle) ? Host(tab.Url) : documentTitle;
            tab.TitleButton.Content = tab.Title;
            tab.TitleButton.ToolTip = tab.Title;
            if (!string.IsNullOrWhiteSpace(tab.Url))
            {
                if (!tab.Owner.privateMode)
                {
                    tab.Owner.preferences.RecentPages.RemoveAll(x => x.Url == tab.Url);
                    tab.Owner.preferences.RecentPages.Insert(0, new RecentPage { Title = tab.Title, Url = tab.Url });
                    if (tab.Owner.preferences.RecentPages.Count > 40) tab.Owner.preferences.RecentPages.RemoveRange(40, tab.Owner.preferences.RecentPages.Count - 40);
                    tab.Owner.SavePreferences();
                }
            }
            if (tab == tab.Owner.activeTab) tab.Owner.UpdateBookmarkButton();
        };
        tab.View.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
            {
                CoreWebView2 core = tab.View.CoreWebView2;
                core.DownloadStarting += (_, download) => tab.Owner.TrackDownload(download);
                _ = core.AddScriptToExecuteOnDocumentCreatedAsync(PagePersistenceScript);
                _ = core.AddScriptToExecuteOnDocumentCreatedAsync(FullScreenKeyScript);
                core.ContainsFullScreenElementChanged += (_, _) =>
                {
                    if (tab == tab.Owner.activeTab) tab.Owner.SyncFullScreen();
                };
                core.WebMessageReceived += (_, message) =>
                {
                    string receivedMessage;
                    try { receivedMessage = message.TryGetWebMessageAsString(); }
                    catch (ArgumentException) { return; } // page posted a non-string message

                    var owner = tab.Owner;
                    if (receivedMessage == "trixter:new-window")
                        owner.OpenBrowserWindow(false);

                    else if (receivedMessage == "trixter:incognito-window")
                        owner.OpenBrowserWindow(true);

                    else if (receivedMessage == "trixter:history")
                        owner.History_Click(owner, new RoutedEventArgs());

                    else if (receivedMessage == "trixter:toggle-fullscreen:" + fullScreenNonce)
                    {
                        if (tab == owner.activeTab) owner.ToggleBrowserFullScreen();
                    }

                    else if (receivedMessage.StartsWith("trixter:password-submitted:") && !owner.privateMode)
                    {
                        if (!Uri.TryCreate(message.Source, UriKind.Absolute, out var sourceUri) ||
                            !Uri.TryCreate(tab.Url, UriKind.Absolute, out var tabUri))
                            return;

                        if (sourceUri.Scheme != Uri.UriSchemeHttp &&
                            sourceUri.Scheme != Uri.UriSchemeHttps)
                            return;

                        string sourceOrigin = sourceUri.GetLeftPart(UriPartial.Authority);
                        string tabOrigin = tabUri.GetLeftPart(UriPartial.Authority);

                        if (!string.Equals(sourceOrigin, tabOrigin,
                            StringComparison.OrdinalIgnoreCase))
                            return;

                        try
                        {
                            string json = receivedMessage[
                                "trixter:password-submitted:".Length..];

                            using var document = JsonDocument.Parse(json);
                            var root = document.RootElement;

                            string username = root.GetProperty("username").GetString() ?? "";
                            string password = root.GetProperty("password").GetString() ?? "";

                            if (string.IsNullOrWhiteSpace(username) ||
                                string.IsNullOrEmpty(password) ||
                                username.Length > 500 ||
                                password.Length > 2000)
                                return;

                            tab.PendingLoginOrigin = sourceOrigin;
                            tab.PendingLoginUsername = username;
                            tab.PendingLoginPassword = password;

                            if (!owner.passwordVault.IsUnlocked)
                                return;

                            // Don't offer to save yet: the login might fail. Wait and
                            // only prompt once the login form is gone.
                            int attempt = ++tab.LoginAttemptId;
                            _ = owner.OfferSaveAfterSuccessfulLogin(tab, attempt);

                        }
                        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
                        {
                            // Ignore invalid messages from webpages.
                        }
                    }
                    else if (receivedMessage == "trixter:show-password-menu" && !owner.privateMode)
                    {
                        if (tab != owner.activeTab || !tab.View.IsVisible)
                            return;

                        if (!Uri.TryCreate(message.Source, UriKind.Absolute, out var sourceUri) ||
                            !Uri.TryCreate(tab.Url, UriKind.Absolute, out var tabUri))
                            return;

                        if (!string.Equals(
                            sourceUri.GetLeftPart(UriPartial.Authority),
                            tabUri.GetLeftPart(UriPartial.Authority),
                            StringComparison.OrdinalIgnoreCase))
                            return;

                        // When locked, show the locked-state prompt without exposing
                        // usernames or passwords. Entries are read only after unlock.
                        var accounts = owner.passwordVault.IsUnlocked
                            ? owner.passwordVault.GetEntriesForWebsite(tab.Url)
                            : new List<VaultEntry>();

                        owner.ShowPasswordAccountMenu(tab, accounts);
                    }
                    else if (receivedMessage == "trixter:hide-password-menu")
                    {
                        owner.passwordAccountPopup?.SetCurrentValue(Popup.IsOpenProperty, false);
                        owner.passwordAccountPopup = null;
                    }
                };
                _ = core.AddScriptToExecuteOnDocumentCreatedAsync(
                    """
                    (() => {
                        let lastPrompt = 0;

                        document.addEventListener('submit', (event) => {
                            const form = event.target;

                            if (!(form instanceof HTMLFormElement))
                                return;

                            const password = form.querySelector('input[type="password"]');

                            if (!password || !password.value)
                                return;

                            const inputs = Array.from(form.querySelectorAll('input'));

                            const username = inputs.find(input =>
                                input !== password &&
                                (
                                    input.autocomplete === 'username' ||
                                    input.type === 'email' ||
                                    /user|email|login|account/i.test(
                                        [
                                            input.name,
                                            input.id,
                                            input.autocomplete,
                                            input.getAttribute('aria-label')
                                        ].join(' ')
                                    )
                                )
                            );

                            if (!username || !username.value)
                                return;

                            window.chrome.webview.postMessage(
                                'trixter:password-submitted:' + JSON.stringify({
                                    username: username.value,
                                    password: password.value
                                })
                            );
                        }, true);

                        document.addEventListener('focusin', (event) => {
                            const target = event.target;
                            if (!document.hasFocus())
                                return;

                            if (!(target instanceof HTMLInputElement))
                                return;

                            const form = target.form;

                            if (!form)
                                return;

                            const password = form.querySelector(
                                'input[type="password"]'
                            );

                            if (!password)
                                return;

                            const fieldName = [
                                target.name,
                                target.id,
                                target.autocomplete,
                                target.getAttribute('aria-label')
                            ].join(' ');

                            const isUsername =
                                target === password ||
                                target.type === 'email' ||
                                target.autocomplete === 'username' ||
                                /email|user|login|account/i.test(fieldName);

                            if (!isUsername)
                                return;

                            const now = Date.now();

                            if (now - lastPrompt < 500)
                                return;

                            lastPrompt = now;

                            window.chrome.webview.postMessage(
                                'trixter:show-password-menu'
                            );
                        });
                        document.addEventListener('pointerdown', (event) => {
                            const target = event.target;

                            const isPasswordField =
                                target instanceof HTMLInputElement &&
                                target.type === 'password';

                            const isUsernameField =
                                target instanceof HTMLInputElement &&
                                (
                                    target.type === 'email' ||
                                    target.autocomplete === 'username' ||
                                    /email|user|login|account/i.test(
                                        [
                                            target.name,
                                            target.id,
                                            target.autocomplete,
                                            target.getAttribute('aria-label')
                                        ].join(' ')
                                    )
                                );

                            if (!isPasswordField && !isUsernameField) {
                                window.chrome.webview.postMessage(
                                    'trixter:hide-password-menu'
                                );
                            }
                        });
                        window.addEventListener('blur', () => {
                            window.chrome.webview.postMessage('trixter:hide-password-menu');
                        });
                    })();
                    """
                );
                core.NewWindowRequested += (_, request) =>
                {
                    request.Handled = true;
                    _ = tab.Owner.AddTab(request.Uri);
                };
            }
        };

        try { await tab.View.EnsureCoreWebView2Async(tab.Owner.privateMode ? tab.Owner.privateEnvironment : tab.Owner.browserEnvironment); }
        catch (Exception ex)
        {
            tabs.Remove(tab);
            TabStrip.Children.Remove(tab.Surface);
            BrowserHost.Children.Remove(tab.View);
            UpdateTabStripLayout();
            tab.View.Dispose();
            ShowWebView2InitializationError(ex);
            return false;
        }
        tab.View.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
        tab.View.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        if (!tab.Owner.privateMode)
        {
            try { await ImportOperaUBlockAsync(tab.View.CoreWebView2.Profile); }
            catch (Exception ex) { MessageBox.Show("Tryxtor found uBlock Origin in Opera GX but could not load it.\n\n" + ex.Message, "Tryxtor Extensions", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        ActivateTab(tab);
        var target = string.IsNullOrWhiteSpace(address) ? preferences.HomePage : address;
        if (!string.IsNullOrWhiteSpace(target)) Navigate(target, tab);
        return true;
    }

    void TabDrag_MouseMove(object sender, MouseEventArgs e)
    {
        BrowserTab? draggedTab = tabDragSource;
        if (draggedTab == null || e.LeftButton != MouseButtonState.Pressed || !tabs.Contains(draggedTab)) return;

        Point screenPoint = PointToScreen(e.GetPosition(this));
        if (!tabDragStarted)
        {
            if (Math.Abs(screenPoint.X - tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(screenPoint.Y - tabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            tabDragStarted = true;
        }

        double tabBarBottom = TitleBar.PointToScreen(new Point(0, TitleBar.ActualHeight)).Y;
        if (screenPoint.Y > tabBarBottom + 18)
        {
            tabDragSource = null;
            _ = TearOffTabAsync(draggedTab);
            e.Handled = true;
            return;
        }

        int targetIndex = -1;
        for (int i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            Point topLeft = tab.Surface.PointToScreen(new Point(0, 0));
            if (new Rect(topLeft, tab.Surface.RenderSize).Contains(screenPoint))
            {
                targetIndex = i;
                break;
            }
        }

        int oldIndex = tabs.IndexOf(draggedTab);
        if (targetIndex >= 0 && targetIndex != oldIndex)
        {
            tabs.RemoveAt(oldIndex);
            tabs.Insert(Math.Min(targetIndex, tabs.Count), draggedTab);
            TabStrip.Children.Remove(draggedTab.Surface);
            TabStrip.Children.Insert(tabs.IndexOf(draggedTab), draggedTab.Surface);
            e.Handled = true;
        }
    }

    void TabDrag_MouseUp(object sender, MouseButtonEventArgs e)
    {
        tabDragSource = null;
        tabDragStarted = false;
    }

    async Task TearOffTabAsync(BrowserTab tab)
    {
        if (!tabs.Contains(tab)) return;

        var detachedWindow = new MainWindow(
            privateMode, null, emptyWindow: true,
            sharedPrivateEnvironment: privateMode ? privateEnvironment : null,
            sharedPrivateProfilePath: privateMode ? privateProfilePath : null);

        detachedWindow.passwordVault = passwordVault;
        detachedWindow.Show();
        bool ready = await detachedWindow.initialTabReady.Task;
        if (!ready)
        {
            detachedWindow.Close();
            return;
        }

        int oldIndex = tabs.IndexOf(tab);
        bool wasActive = tab == activeTab;

        // Detach the existing WebView2 control. It is never recreated or navigated.
        tabs.Remove(tab);
        TabStrip.Children.Remove(tab.Surface);
        UpdateTabStripLayout();
        BrowserHost.Children.Remove(tab.View);

        tab.Owner = detachedWindow;
        detachedWindow.CreateTabHeader(tab);
        detachedWindow.tabs.Add(tab);
        detachedWindow.TabStrip.Children.Add(tab.Surface);
        detachedWindow.UpdateTabStripLayout();
        detachedWindow.BrowserHost.Children.Add(tab.View);
        detachedWindow.ActivateTab(tab);

        if (tabs.Count == 0)
        {
            activeTab = null;
            _ = AddTab();
        }
        else if (wasActive)
        {
            ActivateTab(tabs[Math.Min(oldIndex, tabs.Count - 1)]);
        }

        detachedWindow.Activate();
    }

    void CloseTab(BrowserTab tab)
    {
        int oldIndex = tabs.IndexOf(tab);
        if (oldIndex < 0) return;
        bool wasActive = tab == activeTab;
        tabs.RemoveAt(oldIndex);
        TabStrip.Children.Remove(tab.Surface);
        UpdateTabStripLayout();
        BrowserHost.Children.Remove(tab.View);

        // Removing a WPF control from the visual tree does not release its
        // WebView2 browser/controller resources. Dispose it explicitly.
        if (passwordAccountPopup?.PlacementTarget == tab.View)
        {
            passwordAccountPopup.IsOpen = false;
            passwordAccountPopup = null;
        }
        tab.View.Dispose();

        if (wasActive)
            activeTab = null;
        if (tabs.Count == 0)
        {
            activeTab = null;

            // Keep a blank start tab only when this is the last Tryxtor window.
            bool anotherTryxtorWindowIsOpen = Application.Current.Windows
                .OfType<MainWindow>()
                .Any(window => window != this);

            if (anotherTryxtorWindowIsOpen)
                Close();
            else
                _ = AddTab();

            return;
        }

        if (wasActive) ActivateTab(tabs[Math.Min(oldIndex, tabs.Count - 1)]);
    }

    void ActivateTab(BrowserTab tab)
    {
        if (activeTab != tab)
            ClosePasswordSavePopup();
        if (activeTab != tab && passwordAccountPopup?.IsOpen == true)
        {
            passwordAccountPopup.IsOpen = false;
            passwordAccountPopup = null;
        }
        var previousTab = activeTab;
        if (previousTab != null && previousTab != tab && tabs.Contains(previousTab))
        {
            try
            {
                var core = previousTab.View.CoreWebView2;
                if (core != null)
                {
                    _ = core.ExecuteScriptAsync(
                        "document.activeElement && document.activeElement.blur && document.activeElement.blur();");
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
        activeTab = tab;
        var theme = BuiltInThemes.TryGetValue(
            preferences.ColorTheme,
            out var selectedTheme)
            ? selectedTheme
            : BuiltInThemes["Midnight"];

        Color background = ParseColor(theme.Background);
        Color surface = ParseColor(theme.Surface);
        Brush text = ParseBrush(theme.Text, Brushes.White);
        Brush accent = ParseBrush(preferences.Accent, Brushes.LightGreen);
        foreach (var item in tabs)
        {
            item.View.Visibility = item == tab
                ? Visibility.Visible
                : Visibility.Collapsed;

            item.TitleButton.Foreground = text;

            item.Surface.Background = item == tab
                ? new SolidColorBrush(Blend(ParseColor(preferences.Accent), background, .19))
                : new SolidColorBrush(Blend(surface, background, .5));

            item.Surface.BorderBrush = item == tab
                ? accent
                : Brushes.Transparent;

            item.Surface.BorderThickness = new Thickness(
                0, 0, 0, item == tab ? 2 : 0);
        }
        SetAddressText(tab.Url);
        UpdateBookmarkButton();
        UpdateLoadingIndicator();
        SyncFullScreen();
        NewTabPanel.Visibility = string.IsNullOrEmpty(tab.Url) ? Visibility.Visible : Visibility.Collapsed;
        BrowserHost.Visibility = string.IsNullOrEmpty(tab.Url) ? Visibility.Collapsed : Visibility.Visible;
        if (string.IsNullOrEmpty(tab.Url))
        {
            Dispatcher.BeginInvoke(() =>
            {
                AddressBox.Focus();
                Keyboard.Focus(AddressBox);
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    void NewTab_Click(object sender, RoutedEventArgs e) => _ = AddTab();
}