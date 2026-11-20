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
    static async Task ImportOperaUBlockAsync(CoreWebView2Profile profile)
    {
        await uBlockImportGate.WaitAsync();
        try
        {
            if (uBlockImportAttempted) return;
            uBlockImportAttempted = true;
            if ((await profile.GetBrowserExtensionsAsync()).Any(extension => extension.Id == UBlockExtensionId)) return;

            string operaExtensions = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Opera Software", "Opera GX Stable", "Default", "Extensions", UBlockExtensionId);
            if (!System.IO.Directory.Exists(operaExtensions)) return;

            string? source = System.IO.Directory.GetDirectories(operaExtensions)
                .OrderByDescending(System.IO.Directory.GetLastWriteTimeUtc)
                .FirstOrDefault(path => System.IO.File.Exists(System.IO.Path.Combine(path, "manifest.json")));
            if (source == null) return;

            using (var manifest = JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(source, "manifest.json"))))
            {
                if (!manifest.RootElement.TryGetProperty("name", out var name) || name.GetString() != "uBlock Origin") return;
            }

            string version = System.IO.Path.GetFileName(source);
            string managedFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tryxtor", "Extensions", "uBlockOrigin-" + version);
            if (!System.IO.Directory.Exists(managedFolder)) CopyExtensionDirectory(source, managedFolder);
            await profile.AddBrowserExtensionAsync(managedFolder);
        }
        finally { uBlockImportGate.Release(); }
    }

    static void CopyExtensionDirectory(string source, string destination)
    {
        System.IO.Directory.CreateDirectory(destination);
        foreach (string file in System.IO.Directory.GetFiles(source))
            System.IO.File.Copy(file, System.IO.Path.Combine(destination, System.IO.Path.GetFileName(file)), true);
        foreach (string directory in System.IO.Directory.GetDirectories(source))
        {
            if ((System.IO.File.GetAttributes(directory) & System.IO.FileAttributes.ReparsePoint) != 0) continue;
            CopyExtensionDirectory(directory, System.IO.Path.Combine(destination, System.IO.Path.GetFileName(directory)));
        }
    }

    async void Extensions_Click(object sender, RoutedEventArgs e)
    {
        var profile = activeTab?.View.CoreWebView2?.Profile;
        if (profile == null)
        {
            MessageBox.Show("Wait for a page or new tab to finish opening, then open Extensions again.", "Tryxtor Extensions");
            return;
        }

        bool light = preferences.Theme == "Light";
        var ink = ParseBrush(light ? "#263142" : "#E9EDF3", Brushes.White);
        var muted = ParseBrush(light ? "#687487" : "#9CA7B7", Brushes.Gray);
        var surface = ParseBrush(light ? "#F5F7FA" : "#171B23", Brushes.Black);
        var dialog = new Window { Title = "Extensions", Owner = this, Width = 480, Height = 430, MinHeight = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = surface, Foreground = ink, ResizeMode = ResizeMode.CanResizeWithGrip };
        var layout = new DockPanel { Margin = new Thickness(18) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var install = new Button { Content = "Load unpacked…", Padding = new Thickness(13, 8, 13, 8), Margin = new Thickness(0, 0, 8, 0), Background = ParseBrush(preferences.Accent, Brushes.LightGreen), Foreground = light ? Brushes.White : Brushes.Black, BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold };
        var close = new Button { Content = "Done", Padding = new Thickness(13, 8, 13, 8), Background = ParseBrush(light ? "#E5E9EF" : "#2A303B", Brushes.Gray), Foreground = ink, BorderThickness = new Thickness(0) };
        footer.Children.Add(install); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var list = new StackPanel();
        layout.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        close.Click += (_, _) => dialog.Close();

        async Task RefreshExtensions()
        {
            list.Children.Clear();
            IReadOnlyList<CoreWebView2BrowserExtension> extensions;
            try { extensions = await profile.GetBrowserExtensionsAsync(); }
            catch (Exception ex) { list.Children.Add(new TextBlock { Text = "Could not read installed extensions: " + ex.Message, Foreground = muted, TextWrapping = TextWrapping.Wrap }); return; }
            if (extensions.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = "No extensions installed yet. Load an unpacked extension folder to get started.", Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 12) });
                return;
            }
            foreach (var extension in extensions)
            {
                var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 8), Background = ParseBrush(light ? "#FFFFFF" : "#222833", Brushes.Gray) };
                var remove = new Button { Content = "Remove", Tag = extension, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(4), Background = Brushes.Transparent, Foreground = ink, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
                remove.Click += async (_, _) =>
                {
                    if (remove.Tag is not CoreWebView2BrowserExtension current) return;
                    try { await current.RemoveAsync(); await RefreshExtensions(); }
                    catch (Exception ex) { MessageBox.Show("Could not remove the extension: " + ex.Message, "Tryxtor Extensions"); }
                };
                DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                var enabled = new CheckBox { Content = extension.Name, IsChecked = extension.IsEnabled, Foreground = ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 8, 5, 8), FontWeight = FontWeights.SemiBold };
                enabled.Checked += async (_, _) => await SetExtensionEnabled(extension, true);
                enabled.Unchecked += async (_, _) => await SetExtensionEnabled(extension, false);
                row.Children.Add(enabled);
                row.ToolTip = "Extension ID: " + extension.Id;
                list.Children.Add(row);
            }
        }

        async Task SetExtensionEnabled(CoreWebView2BrowserExtension extension, bool enabled)
        {
            try { await extension.EnableAsync(enabled); }
            catch (Exception ex) { MessageBox.Show("Could not update the extension: " + ex.Message, "Tryxtor Extensions"); }
        }

        install.Click += async (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Title = "Choose the extension's manifest.json", Filter = "Extension manifest|manifest.json|JSON files|*.json", FileName = "manifest.json", CheckFileExists = true };
            if (picker.ShowDialog(dialog) != true) return;
            string? folder = System.IO.Path.GetDirectoryName(picker.FileName);
            if (string.IsNullOrWhiteSpace(folder)) return;
            try
            {
                var added = await profile.AddBrowserExtensionAsync(folder);
                await RefreshExtensions();
                MessageBox.Show($"{added.Name} was added to this Tryxtor profile.", "Tryxtor Extensions", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show("Could not install this extension. Choose its unpacked folder containing manifest.json.\n\n" + ex.Message, "Tryxtor Extensions", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };

        dialog.Content = layout;
        await RefreshExtensions();
        dialog.ShowDialog();
    }
}
