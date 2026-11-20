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
    enum DownloadStatus { Downloading, Paused, Completed, Failed, Cancelled }

    sealed class DownloadItem
    {
        public CoreWebView2DownloadOperation? Operation;
        public string FileName = "";
        public string Url = "";
        public string FilePath = "";
        public long BytesReceived;
        public long? TotalBytes;
        public DownloadStatus Status = DownloadStatus.Downloading;
        public string? Error;
        public DateTime Started = DateTime.Now;
        public bool Incognito;
        public double BytesPerSecond;
        public DateTime LastSampleTime = DateTime.UtcNow;
        public long LastSampleBytes;
    }

    sealed class DownloadRecord
    {
        public string FileName { get; set; } = "";
        public string Url { get; set; } = "";
        public string FilePath { get; set; } = "";
        public long Size { get; set; }
        public string Status { get; set; } = "";
        public string? Error { get; set; }
        public DateTime Started { get; set; }
    }

    // Shared by every browser window so downloads show up everywhere.
    static readonly List<DownloadItem> downloadItems = new();

    static bool downloadsLoaded;

    static readonly string downloadsPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tryxtor", "downloads.json");

    static event Action? DownloadsListChanged;   // item added or removed

    static event Action? DownloadsActivity;      // progress or state changed

    private Window? downloadsWindow;

    static void EnsureDownloadsLoaded()
    {
        if (downloadsLoaded) return;
        downloadsLoaded = true;
        try
        {
            if (!System.IO.File.Exists(downloadsPath)) return;
            var records = JsonSerializer.Deserialize<List<DownloadRecord>>(System.IO.File.ReadAllText(downloadsPath)) ?? new();
            foreach (var record in records)
            {
                var status = Enum.TryParse<DownloadStatus>(record.Status, out var parsed) ? parsed : DownloadStatus.Failed;
                string? error = record.Error;
                if (status is DownloadStatus.Downloading or DownloadStatus.Paused)
                {
                    status = DownloadStatus.Failed;
                    error ??= "Interrupted when Tryxtor closed";
                }
                downloadItems.Add(new DownloadItem
                {
                    FileName = record.FileName,
                    Url = record.Url,
                    FilePath = record.FilePath,
                    BytesReceived = record.Size,
                    TotalBytes = record.Size > 0 ? record.Size : null,
                    Status = status,
                    Error = error,
                    Started = record.Started
                });
            }
        }
        catch { /* a corrupt downloads list should never stop the browser */ }
    }

    static void SaveDownloads()
    {
        try
        {
            var records = downloadItems
                .Where(item => !item.Incognito)
                .Take(200)
                .Select(item => new DownloadRecord
                {
                    FileName = item.FileName,
                    Url = item.Url,
                    FilePath = item.FilePath,
                    Size = item.TotalBytes ?? item.BytesReceived,
                    Status = item.Status.ToString(),
                    Error = item.Error,
                    Started = item.Started
                })
                .ToList();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(downloadsPath)!);
            System.IO.File.WriteAllText(downloadsPath, JsonSerializer.Serialize(records));
        }
        catch { /* ignore disk errors */ }
    }

    static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    void TrackDownload(CoreWebView2DownloadStartingEventArgs e)
    {
        EnsureDownloadsLoaded();
        var operation = e.DownloadOperation;

        // Handled = true hides WebView2's built-in download flyout; the downloads
        // page replaces it. The file still saves to the default Downloads folder.
        e.Handled = true;

        var item = new DownloadItem
        {
            Operation = operation,
            Url = operation.Uri,
            FilePath = operation.ResultFilePath,
            FileName = System.IO.Path.GetFileName(operation.ResultFilePath),
            Incognito = privateMode
        };
        if (operation.TotalBytesToReceive is ulong total && total > 0) item.TotalBytes = (long)total;

        void SyncState()
        {
            item.FilePath = operation.ResultFilePath;
            item.FileName = System.IO.Path.GetFileName(operation.ResultFilePath);
            item.BytesReceived = operation.BytesReceived;
            if (operation.TotalBytesToReceive is ulong knownTotal && knownTotal > 0) item.TotalBytes = (long)knownTotal;

            switch (operation.State)
            {
                case CoreWebView2DownloadState.InProgress:
                    item.Status = DownloadStatus.Downloading;
                    item.LastSampleTime = DateTime.UtcNow;
                    item.LastSampleBytes = item.BytesReceived;
                    break;
                case CoreWebView2DownloadState.Completed:
                    item.Status = DownloadStatus.Completed;
                    item.BytesPerSecond = 0;
                    if (item.TotalBytes == null) item.TotalBytes = item.BytesReceived;
                    break;
                default:
                    var reason = operation.InterruptReason;
                    item.BytesPerSecond = 0;
                    if (reason == CoreWebView2DownloadInterruptReason.UserPaused) item.Status = DownloadStatus.Paused;
                    else if (reason == CoreWebView2DownloadInterruptReason.UserCanceled) item.Status = DownloadStatus.Cancelled;
                    else { item.Status = DownloadStatus.Failed; item.Error = reason.ToString(); }
                    break;
            }

            if (item.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled)
                SaveDownloads();
            DownloadsActivity?.Invoke();
        }

        operation.BytesReceivedChanged += (_, _) =>
        {
            item.BytesReceived = operation.BytesReceived;
            if (operation.TotalBytesToReceive is ulong knownTotal && knownTotal > 0) item.TotalBytes = (long)knownTotal;
            double seconds = (DateTime.UtcNow - item.LastSampleTime).TotalSeconds;
            if (seconds >= 1)
            {
                item.BytesPerSecond = (item.BytesReceived - item.LastSampleBytes) / seconds;
                item.LastSampleTime = DateTime.UtcNow;
                item.LastSampleBytes = item.BytesReceived;
            }
            DownloadsActivity?.Invoke();
        };
        operation.StateChanged += (_, _) => SyncState();

        downloadItems.Insert(0, item);
        DownloadsListChanged?.Invoke();
        DownloadsActivity?.Invoke();
    }

    void UpdateDownloadsIndicator()
    {
        bool active = downloadItems.Any(d => (!d.Incognito || privateMode) && d.Status == DownloadStatus.Downloading);
        var theme = BuiltInThemes.TryGetValue(preferences.ColorTheme, out var selected) ? selected : BuiltInThemes["Midnight"];
        DownloadsButton.Foreground = active
            ? ParseBrush(preferences.Accent, Brushes.LightGreen)
            : ParseBrush(theme.Text, Brushes.White);
    }

    void Downloads_Click(object sender, RoutedEventArgs e)
    {
        if (downloadsWindow != null)
        {
            downloadsWindow.Activate();
            return;
        }

        EnsureDownloadsLoaded();
        bool light = preferences.Theme == "Light";
        var ink = ParseBrush(light ? "#263142" : "#E9EDF3", Brushes.White);
        var muted = ParseBrush(light ? "#687487" : "#9CA7B7", Brushes.Gray);
        var surface = ParseBrush(light ? "#F5F7FA" : "#171B23", Brushes.Black);
        var card = ParseBrush(light ? "#FFFFFF" : "#1B2029", Brushes.Black);
        var outline = ParseBrush(light ? "#D2D8E1" : "#343C48", Brushes.Gray);
        var accent = ParseBrush(preferences.Accent, Brushes.LightGreen);

        var dialog = new Window
        {
            Title = "Downloads",
            Owner = this,
            Width = 640,
            Height = 600,
            MinWidth = 420,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = surface,
            Foreground = ink,
            ResizeMode = ResizeMode.CanResizeWithGrip
        };
        downloadsWindow = dialog;

        var layout = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = "Downloads", FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = ink });
        header.Children.Add(new TextBlock { Text = "Files you download are listed here.", Foreground = muted, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        Button MakeButton(string text, string? tip = null) => new Button
        {
            Content = text,
            ToolTip = tip,
            Padding = new Thickness(11, 5, 11, 5),
            Margin = new Thickness(0, 0, 6, 0),
            Background = card,
            Foreground = ink,
            BorderBrush = outline,
            BorderThickness = new Thickness(1)
        };

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var clearFinished = MakeButton("Clear finished");
        clearFinished.Padding = new Thickness(12, 8, 12, 8);
        var openFolder = MakeButton("Open downloads folder");
        openFolder.Padding = new Thickness(12, 8, 12, 8);
        var done = MakeButton("Done");
        done.Padding = new Thickness(15, 8, 15, 8);
        done.Margin = new Thickness(0);
        done.Background = accent;
        done.Foreground = light ? Brushes.White : Brushes.Black;
        done.BorderThickness = new Thickness(0);
        done.FontWeight = FontWeights.SemiBold;
        footer.Children.Add(clearFinished);
        footer.Children.Add(openFolder);
        footer.Children.Add(done);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);

        var list = new StackPanel();
        layout.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var refreshers = new List<Action>();

        Border BuildRow(DownloadItem item)
        {
            var title = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis };
            var detail = new TextBlock { FontSize = 11, Foreground = muted, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            var bar = new ProgressBar { Height = 4, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 9, 0, 0), Foreground = accent, Background = outline, BorderThickness = new Thickness(0) };

            var pause = MakeButton("Pause");
            var resume = MakeButton("Resume");
            var cancel = MakeButton("Cancel");
            var open = MakeButton("Open");
            var folder = MakeButton("Show in folder");

            var remove = new Button
            {
                Content = "x",
                ToolTip = "Remove from list (keeps the file)",
                Padding = new Thickness(9, 4, 9, 4),
                Background = Brushes.Transparent,
                Foreground = muted,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top
            };

            pause.Click += (_, _) => { try { item.Operation?.Pause(); } catch { } };
            resume.Click += (_, _) => { try { item.Operation?.Resume(); } catch { } };
            cancel.Click += (_, _) => { try { item.Operation?.Cancel(); } catch { } };
            open.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.FilePath) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show("Could not open the file:\n\n" + ex.Message, "Downloads"); }
            };
            folder.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FilePath}\""); }
                catch (Exception ex) { MessageBox.Show("Could not open the folder:\n\n" + ex.Message, "Downloads"); }
            };
            remove.Click += (_, _) =>
            {
                if (item.Status is DownloadStatus.Downloading or DownloadStatus.Paused)
                    try { item.Operation?.Cancel(); } catch { }
                downloadItems.Remove(item);
                SaveDownloads();
                DownloadsListChanged?.Invoke();
            };

            var info = new StackPanel();
            info.Children.Add(title);
            info.Children.Add(detail);
            info.Children.Add(bar);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var b in new[] { pause, resume, cancel, open, folder }) buttons.Children.Add(b);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumnSpan(buttons, 2);
            Grid.SetRow(buttons, 1);
            Grid.SetColumn(remove, 1);
            grid.Children.Add(info);
            grid.Children.Add(remove);
            grid.Children.Add(buttons);

            string host = Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) ? uri.Host : "";
            DownloadStatus? lastStatus = null;
            bool fileExists = false;

            void Refresh()
            {
                bool active = item.Status == DownloadStatus.Downloading;
                bool paused = item.Status == DownloadStatus.Paused;

                // Finished rows don't change, so skip them (and the disk check) until their status does.
                if (!active && !paused && lastStatus == item.Status) return;
                if (lastStatus != item.Status)
                    fileExists = item.Status == DownloadStatus.Completed && System.IO.File.Exists(item.FilePath);
                lastStatus = item.Status;

                title.Text = item.FileName;
                title.ToolTip = item.FilePath;

                string sizeText = FormatBytes(item.BytesReceived) + (item.TotalBytes is long total ? " of " + FormatBytes(total) : "");
                string hostText = host.Length > 0 ? " · " + host : "";
                detail.Text = item.Status switch
                {
                    DownloadStatus.Downloading => sizeText
                        + (item.BytesPerSecond > 0 ? " · " + FormatBytes((long)item.BytesPerSecond) + "/s" : "")
                        + hostText,
                    DownloadStatus.Paused => "Paused · " + sizeText,
                    DownloadStatus.Completed => fileExists
                        ? FormatBytes(item.TotalBytes ?? item.BytesReceived) + hostText + " · " + item.Started.ToString("g")
                        : "File was moved or deleted",
                    DownloadStatus.Cancelled => "Cancelled" + hostText,
                    _ => "Failed" + (string.IsNullOrEmpty(item.Error) ? "" : " (" + item.Error + ")") + hostText
                };

                bar.Visibility = active || paused ? Visibility.Visible : Visibility.Collapsed;
                bar.IsIndeterminate = active && !(item.TotalBytes > 0);
                if (item.TotalBytes is long t && t > 0)
                    bar.Value = Math.Min(1.0, (double)item.BytesReceived / t);

                pause.Visibility = active && item.Operation != null ? Visibility.Visible : Visibility.Collapsed;
                resume.Visibility = paused && item.Operation?.CanResume == true ? Visibility.Visible : Visibility.Collapsed;
                cancel.Visibility = (active || paused) && item.Operation != null ? Visibility.Visible : Visibility.Collapsed;
                open.Visibility = folder.Visibility = fileExists ? Visibility.Visible : Visibility.Collapsed;
                buttons.Visibility = pause.Visibility == Visibility.Visible || resume.Visibility == Visibility.Visible
                    || cancel.Visibility == Visibility.Visible || fileExists
                    ? Visibility.Visible : Visibility.Collapsed;
            }

            refreshers.Add(Refresh);
            return new Border
            {
                Child = grid,
                Background = card,
                BorderBrush = outline,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 8, 12),
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        void Render()
        {
            refreshers.Clear();
            list.Children.Clear();
            var visible = downloadItems.Where(d => !d.Incognito || privateMode).ToList();
            if (visible.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "No downloads yet.",
                    Foreground = muted,
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 60, 0, 0)
                });
                return;
            }
            foreach (var item in visible) list.Children.Add(BuildRow(item));
            foreach (var refresh in refreshers.ToList()) refresh();
        }

        clearFinished.Click += (_, _) =>
        {
            downloadItems.RemoveAll(d => (!d.Incognito || privateMode) &&
                d.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled);
            SaveDownloads();
            DownloadsListChanged?.Invoke();
        };
        openFolder.Click += (_, _) =>
        {
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("Could not open the folder:\n\n" + ex.Message, "Downloads"); }
        };
        done.Click += (_, _) => dialog.Close();

        Action onActivity = () => { foreach (var refresh in refreshers.ToList()) refresh(); };
        Action onListChanged = Render;
        DownloadsActivity += onActivity;
        DownloadsListChanged += onListChanged;
        dialog.Closed += (_, _) =>
        {
            DownloadsActivity -= onActivity;
            DownloadsListChanged -= onListChanged;
            downloadsWindow = null;
        };

        Render();
        dialog.Content = layout;
        dialog.Show();
    }
}
