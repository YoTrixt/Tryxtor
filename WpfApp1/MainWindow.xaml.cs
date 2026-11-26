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

public partial class MainWindow : Window
{
    static readonly object browserEnvironmentLock = new();

    static Task<CoreWebView2Environment>? browserEnvironmentTask;

    static readonly string browserUserDataFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tryxtor", "WebView2");

    static readonly SemaphoreSlim uBlockImportGate = new(1, 1);

    static bool uBlockImportAttempted;

    const string UBlockExtensionId = "cjpalhdlnbpafiamejdnhcphjbkeiagm";

    readonly List<BrowserTab> tabs = new();

    readonly string settingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tryxtor", "settings.json");

    Preferences preferences = new();

    readonly bool privateMode;

    string? privateProfilePath;

    CoreWebView2Environment? browserEnvironment;

    CoreWebView2Environment? privateEnvironment;

    BrowserTab? activeTab;

    Grid rootGrid = null!;

    List<AddressSuggestion> currentSuggestions = new();

    bool settingAddressText;

    readonly TaskCompletionSource<bool> initialTabReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    BrowserTab? tabDragSource;

    Point tabDragStart;

    bool tabDragStarted;

    public MainWindow() : this(false, null) { }

    MainWindow(bool privateMode) : this(privateMode, null) { }

    MainWindow(bool privateMode, string? initialAddress, bool emptyWindow = false,
        CoreWebView2Environment? sharedPrivateEnvironment = null, string? sharedPrivateProfilePath = null)
    {
        EnsureThemeDictionary();   // themed control styles must exist before the XAML is parsed
        InitializeComponent();
        AddressBox.GotKeyboardFocus += AddressBox_GotKeyboardFocus;
        AddressBox.LostKeyboardFocus += AddressBox_LostKeyboardFocus;
        this.privateMode = privateMode;
        DownloadsActivity += UpdateDownloadsIndicator;
        Closing += (_, _) =>
        {
            DownloadsActivity -= UpdateDownloadsIndicator;
            // A window can close while it still owns tabs. Release every WebView2
            // before WPF tears down the window and its hosted native windows.
            if (passwordAccountPopup != null)
            {
                passwordAccountPopup.IsOpen = false;
                passwordAccountPopup = null;
            }
            if (passwordSavePopup != null)
            {
                ClosePasswordSavePopup();
                passwordSavePopup = null;
            }

            foreach (var openTab in tabs.ToList())
            {
                TabStrip.Children.Remove(openTab.Surface);
                BrowserHost.Children.Remove(openTab.View);
                openTab.View.Dispose();
            }

            tabs.Clear();
            UpdateTabStripLayout();
            activeTab = null;
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessageHook);
        StateChanged += (_, _) =>
        {
            UpdateResizeBorder();
            UpdateTabTopGap();
            if (MaximizeIcon != null)
                MaximizeIcon.Data = WindowState == WindowState.Maximized
                    ? Geometry.Parse("M 5,5 L 16,5 L 16,16 L 5,16 Z M 2,12 L 2,2 L 12,2")
                    : Geometry.Parse("M 3,3 L 15,3 L 15,15 L 3,15 Z");
        };
        if (privateMode)
        {
            Title = "Tryxtor — Incognito";
            BrandLabel.Text = "TRYXTOR  ·  INCOGNITO";
            privateProfilePath = sharedPrivateProfilePath ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Tryxtor-InPrivate-" + Guid.NewGuid().ToString("N"));
            Closed += (_, _) =>
            {
                // Delete the shared incognito profile only after its final window closes.
                bool anotherWindowUsesProfile = Application.Current.Windows
                    .OfType<MainWindow>()
                    .Any(window => window != this && window.privateMode &&
                        string.Equals(window.privateProfilePath, privateProfilePath, StringComparison.OrdinalIgnoreCase));

                if (!anotherWindowUsesProfile)
                    _ = DeletePrivateProfileWhenReleased(privateProfilePath);
            };
        }
        rootGrid = (Grid)Content;
        PreviewMouseMove += TabDrag_MouseMove;
        PreviewMouseLeftButtonUp += TabDrag_MouseUp;
        BuildSearchEngineIcons();
        FitToWorkArea();
        WindowState = WindowState.Maximized;
        MaximizeIcon.Data = Geometry.Parse(
            "M 5,5 L 16,5 L 16,16 L 5,16 Z M 2,12 L 2,2 L 12,2");
        LoadPreferences();
        SearchEngineBox.SelectedItem = SearchEngineBox.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == preferences.SearchEngine)
            ?? SearchEngineBox.Items.OfType<ComboBoxItem>().First();
        ApplyAppearance();
        Loaded += async (_, _) =>
        {
            RenderShortcuts();
            RenderBookmarks();
            if (privateMode)
            {
                try
                {
                    privateEnvironment = sharedPrivateEnvironment ?? await CoreWebView2Environment.CreateAsync(
                        null, privateProfilePath,
                        new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true });
                }
                catch (Exception ex)
                {
                    ShowWebView2InitializationError(ex);
                    initialTabReady.TrySetResult(false);
                    return;
                }
            }
            else
            {
                try { browserEnvironment = await GetBrowserEnvironmentAsync(); }
                catch (Exception ex)
                {
                    ShowWebView2InitializationError(ex);
                    initialTabReady.TrySetResult(false);
                    return;
                }
            }
            if (emptyWindow)
            {
                initialTabReady.TrySetResult(true);
                return;
            }

            initialTabReady.TrySetResult(await AddTab(initialAddress));
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && passwordAccountPopup?.IsOpen == true)
            {
                passwordAccountPopup.IsOpen = false;
                e.Handled = true;
            }
        };
        PreviewKeyDown += HandleShortcuts;
    }

    static Task<CoreWebView2Environment> GetBrowserEnvironmentAsync()
    {
        lock (browserEnvironmentLock)
            return browserEnvironmentTask ??= CreateBrowserEnvironmentAsync();
    }

    static async Task<CoreWebView2Environment> CreateBrowserEnvironmentAsync()
    {
        System.IO.Directory.CreateDirectory(browserUserDataFolder);
        return await CoreWebView2Environment.CreateAsync(
            null,
            browserUserDataFolder,
            new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true });
    }

    static bool IsWebView2RuntimeMissing(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is WebView2RuntimeNotFoundException) return true;
        return false;
    }

    void ShowWebView2InitializationError(Exception exception)
    {
        if (!IsWebView2RuntimeMissing(exception))
        {
            MessageBox.Show(
                "Tryxtor couldn't start WebView2.\n\n" + exception.Message,
                "Tryxtor Browser Startup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        const string downloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
        var message = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20),
            FontSize = 14
        };
        message.Inlines.Add(new System.Windows.Documents.Run(
            "Tryxtor needs the Microsoft Edge WebView2 Evergreen Runtime to display web pages. It isn't installed on this computer.\n\n"));
        var downloadLink = new System.Windows.Documents.Hyperlink(
            new System.Windows.Documents.Run("Download the WebView2 Runtime from Microsoft"))
        {
            NavigateUri = new Uri(downloadUrl)
        };
        downloadLink.RequestNavigate += (_, args) =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(args.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            args.Handled = true;
        };
        message.Inlines.Add(downloadLink);

        var dialog = new Window()
        {
            Title = "WebView2 Runtime Required",
            Owner = this,
            Width = 470,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Content = message
        };
        dialog.ShowDialog();
    }

    void OpenBrowserWindow(bool incognito)
    {
        var window = new MainWindow(incognito);
        window.Show();
        window.Activate();
    }

    static async Task DeletePrivateProfileWhenReleased(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath)) return;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try { if (System.IO.Directory.Exists(profilePath)) System.IO.Directory.Delete(profilePath, true); return; }
            catch { await Task.Delay(500); }
        }
    }

    void HandleShortcuts(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.N) { e.Handled = true; OpenBrowserWindow(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.H) { e.Handled = true; History_Click(sender, new RoutedEventArgs()); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.J) { e.Handled = true; Downloads_Click(sender, new RoutedEventArgs()); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T) { e.Handled = true; _ = AddTab(); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W && activeTab != null) { e.Handled = true; CloseTab(activeTab); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L) { e.Handled = true; AddressBox.Focus(); AddressBox.SelectAll(); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R) { e.Handled = true; Reload_Click(sender, new RoutedEventArgs()); }
        else if (e.Key == Key.F11 && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; ToggleBrowserFullScreen(); }
        else if (e.Key == Key.Escape) Keyboard.ClearFocus();
    }
}