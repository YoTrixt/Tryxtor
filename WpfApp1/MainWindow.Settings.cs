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
    static ControlTemplate CreateSettingsButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "ButtonBorder";

        border.SetValue(
            Border.BackgroundProperty,
            new TemplateBindingExtension(Button.BackgroundProperty));

        border.SetValue(
            Border.BorderBrushProperty,
            new TemplateBindingExtension(Button.BorderBrushProperty));

        border.SetValue(
            Border.BorderThicknessProperty,
            new TemplateBindingExtension(Button.BorderThicknessProperty));

        border.SetValue(
            Border.CornerRadiusProperty,
            new CornerRadius(7));

        border.SetValue(
            Border.PaddingProperty,
            new TemplateBindingExtension(Button.PaddingProperty));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));

        content.SetValue(
            ContentPresenter.HorizontalAlignmentProperty,
            HorizontalAlignment.Center);

        content.SetValue(
            ContentPresenter.VerticalAlignmentProperty,
            VerticalAlignment.Center);

        content.SetValue(
            ContentPresenter.ContentProperty,
            new TemplateBindingExtension(Button.ContentProperty));

        border.AppendChild(content);
        template.VisualTree = border;

        var hover = new Trigger
        {
            Property = Button.IsMouseOverProperty,
            Value = true
        };

        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85));

        var pressed = new Trigger
        {
            Property = Button.IsPressedProperty,
            Value = true
        };

        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.7));

        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);

        return template;
    }

    string settingsCategory = "Appearance";

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        bool light = preferences.Theme == "Light";
        var bg = new SolidColorBrush(light ? Color.FromRgb(244, 246, 249) : Color.FromRgb(17, 20, 27));
        var card = new SolidColorBrush(light ? Colors.White : Color.FromRgb(27, 32, 41));
        var ink = new SolidColorBrush(light ? Color.FromRgb(32, 39, 49) : Color.FromRgb(239, 242, 247));
        var muted = new SolidColorBrush(light ? Color.FromRgb(83, 94, 108) : Color.FromRgb(161, 170, 183));
        var field = new SolidColorBrush(light ? Color.FromRgb(248, 249, 251) : Color.FromRgb(19, 23, 30));
        var outline = new SolidColorBrush(light ? Color.FromRgb(210, 216, 225) : Color.FromRgb(52, 60, 72));
        var accent = ParseBrush(preferences.Accent, Brushes.LightGreen);
        var dialog = new Window { Title = "Settings", Owner = this, Width = 800, Height = 740, MinHeight = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = bg, Foreground = ink, ResizeMode = ResizeMode.CanResizeWithGrip };
        var settingsButtonStyle = new Style(typeof(Button));

        settingsButtonStyle.Setters.Add(
            new Setter(Button.BackgroundProperty, Brushes.Transparent));

        settingsButtonStyle.Setters.Add(
            new Setter(Button.BorderThicknessProperty, new Thickness(0)));

        settingsButtonStyle.Setters.Add(
            new Setter(Button.FocusVisualStyleProperty, null));

        settingsButtonStyle.Setters.Add(
            new Setter(Button.TemplateProperty, CreateSettingsButtonTemplate()));

        dialog.Resources.Add(typeof(Button), settingsButtonStyle);
        var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel { Margin = new Thickness(26, 22, 26, 18) };
        heading.Children.Add(new TextBlock { Text = "Settings", FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = ink });
        heading.Children.Add(new TextBlock { Text = "Shape Tryxtor around the way you browse.", FontSize = 12, Foreground = muted, Margin = new Thickness(0, 4, 0, 0) });
        layout.Children.Add(heading);

        var appearanceContent = new StackPanel();
        var startPageContent = new StackPanel();
        var shortcutsContent = new StackPanel();
        var passwordContent = new StackPanel();
        string selectedColorTheme = BuiltInThemes.ContainsKey(preferences.ColorTheme)
            ? preferences.ColorTheme
            : "Midnight";
        string selectedDensity = preferences.Density;
        var hex = new TextBox { Text = preferences.Accent, Padding = new Thickness(9), Background = field, Foreground = ink, BorderBrush = outline };

        var themeChoices = new WrapPanel
        {
            Margin = new Thickness(-4, 4, 0, 10)
        };

        var themeBorders = new Dictionary<string, Border>();

        void UpdateThemeSelection()
        {
            foreach (var item in themeBorders)
            {
                bool selected = item.Key == selectedColorTheme;

                item.Value.BorderBrush = selected
                    ? ParseBrush(preferences.Accent, Brushes.LightGreen)
                    : outline;

                item.Value.BorderThickness = new Thickness(selected ? 2 : 1);
            }
        }

        foreach (var theme in BuiltInThemes.Values)
        {
            var themePreview = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var colorPreview = new StackPanel
            {
                Width = 35,
                Height = 35,
                Background = ParseBrush(theme.Background, Brushes.Black),
                Margin = new Thickness(0, 0, 9, 0)
            };

            colorPreview.Children.Add(new Border
            {
                Height = 11,
                Background = ParseBrush(theme.Surface, Brushes.Gray)
            });

            colorPreview.Children.Add(new Border
            {
                Height = 12,
                Margin = new Thickness(5, 5, 5, 0),
                Background = ParseBrush(preferences.Accent, Brushes.LightGreen)
            });

            themePreview.Children.Add(colorPreview);

            themePreview.Children.Add(new TextBlock
            {
                Text = theme.Name,
                Foreground = ParseBrush(theme.Text, Brushes.White),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var previewBorder = new Border
            {
                Width = 155,
                Height = 57,
                Margin = new Thickness(4),
                Padding = new Thickness(7),
                CornerRadius = new CornerRadius(8),
                Background = ParseBrush(theme.Surface, Brushes.Gray),
                BorderBrush = outline,
                BorderThickness = new Thickness(1),
                Child = themePreview
            };

            themeBorders[theme.Name] = previewBorder;

            var themeButton = new Button
            {
                Content = previewBorder,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Select " + theme.Name
            };

            themeButton.Click += (_, _) =>
            {
                selectedColorTheme = theme.Name;
                UpdateThemeSelection();
            };

            themeChoices.Children.Add(themeButton);
        }

        UpdateThemeSelection();

        var densityChoices = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        densityChoices.ColumnDefinitions.Add(new ColumnDefinition());
        densityChoices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
        densityChoices.ColumnDefinitions.Add(new ColumnDefinition());
        densityChoices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
        densityChoices.ColumnDefinitions.Add(new ColumnDefinition());
        var compactChoice = MakeDensityChoice("Compact", selectedDensity == "Compact", ink, muted, outline);
        var balancedChoice = MakeDensityChoice("Balanced", selectedDensity == "Balanced", ink, muted, outline);
        var roomyChoice = MakeDensityChoice("Roomy", selectedDensity == "Roomy", ink, muted, outline);
        Grid.SetColumn(balancedChoice, 2); Grid.SetColumn(roomyChoice, 4);
        densityChoices.Children.Add(compactChoice); densityChoices.Children.Add(balancedChoice); densityChoices.Children.Add(roomyChoice);
        void PickDensity(string value)
        {
            selectedDensity = value;
            var currentAccent = ParseBrush(hex.Text, accent);
            SetDensityChoiceState(compactChoice, selectedDensity == "Compact", currentAccent, outline);
            SetDensityChoiceState(balancedChoice, selectedDensity == "Balanced", currentAccent, outline);
            SetDensityChoiceState(roomyChoice, selectedDensity == "Roomy", currentAccent, outline);
        }
        compactChoice.Click += (_, _) => PickDensity("Compact");
        balancedChoice.Click += (_, _) => PickDensity("Balanced");
        roomyChoice.Click += (_, _) => PickDensity("Roomy");
        var wheel = CreateColorWheel();
        var wheelMarker = new Ellipse { Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false };
        wheel.Children.Add(wheelMarker);
        void PlaceWheelMarker(Color color)
        {
            Point position = ColorToWheelPoint(color, wheel.Width, wheel.Height);
            Canvas.SetLeft(wheelMarker, position.X - wheelMarker.Width / 2);
            Canvas.SetTop(wheelMarker, position.Y - wheelMarker.Height / 2);
        }
        PlaceWheelMarker(ParseColor(preferences.Accent));
        void PickWheelColor(object? _, MouseEventArgs e)
        {
            Point point = e.GetPosition(wheel);
            Color picked = ColorAtWheelPoint(point, wheel.Width, wheel.Height);
            hex.Text = picked.ToString();
            PlaceWheelMarker(picked);
        }
        wheel.MouseLeftButtonDown += (s, e) => { wheel.CaptureMouse(); PickWheelColor(s, e); e.Handled = true; };
        wheel.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) PickWheelColor(s, e); };
        wheel.MouseLeftButtonUp += (_, _) => wheel.ReleaseMouseCapture();
        hex.TextChanged += (_, _) =>
        {
            if (!IsColor(hex.Text)) return;
            Color color = ParseColor(hex.Text);
            PlaceWheelMarker(color);
            var selectedAccent = new SolidColorBrush(color);
            SetDensityChoiceState(compactChoice, selectedDensity == "Compact", selectedAccent, outline);
            SetDensityChoiceState(balancedChoice, selectedDensity == "Balanced", selectedAccent, outline);
            SetDensityChoiceState(roomyChoice, selectedDensity == "Roomy", selectedAccent, outline);
        };
        var swatches = new WrapPanel { Margin = new Thickness(-4, 3, 0, 8) };
        foreach (string color in new[] { "#B6F36A", "#83C9FF", "#C2A0FF", "#FF9E80", "#FFCF70", "#FF80AA", "#66DBC7" })
        {
            var swatch = new Button { Content = " ", Width = 27, Height = 27, Margin = new Thickness(4), Background = ParseBrush(color, Brushes.White), BorderThickness = new Thickness(0), ToolTip = color };
            swatch.Click += (_, _) => hex.Text = color;
            swatches.Children.Add(swatch);
        }
        var bookmarkCheck = new CheckBox { Content = "Show bookmarks bar", IsChecked = preferences.ShowBookmarks, Foreground = ink, Margin = new Thickness(0, 0, 0, 3) };
        var shortcutCheck = new CheckBox { Content = "Show shortcuts on start page", IsChecked = preferences.ShowShortcuts, Foreground = ink, Margin = new Thickness(0, 0, 0, 3) };
        var suggestionCheck = new CheckBox { Content = "Show address suggestions while typing", IsChecked = preferences.ShowSuggestions, Foreground = ink, Margin = new Thickness(0, 0, 0, 3) };
        var home = new TextBox { Text = preferences.HomePage, Padding = new Thickness(9), Background = field, Foreground = ink, BorderBrush = outline };
        string selectedBackgroundPath = preferences.StartPageBackground;
        var backgroundStatus = new TextBlock { Text = string.IsNullOrWhiteSpace(selectedBackgroundPath) ? "No background image" : System.IO.Path.GetFileName(selectedBackgroundPath), Foreground = muted, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        var chooseBackground = new Button { Content = "Choose image…", Padding = new Thickness(11, 7, 11, 7), Foreground = ink, Background = field, BorderBrush = outline, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        chooseBackground.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Title = "Choose a start page background", Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*", CheckFileExists = true };
            if (picker.ShowDialog(dialog) == true)
            {
                selectedBackgroundPath = picker.FileName;
                backgroundStatus.Text = System.IO.Path.GetFileName(selectedBackgroundPath);
            }
        };
        var clearBackground = new Button { Content = "Remove", Padding = new Thickness(10, 7, 10, 7), Foreground = ink, Background = field, BorderBrush = outline, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Margin = new Thickness(7, 0, 0, 0) };
        clearBackground.Click += (_, _) => { selectedBackgroundPath = ""; backgroundStatus.Text = "No background image"; };
        var backgroundControls = new StackPanel { Orientation = Orientation.Horizontal };
        backgroundControls.Children.Add(chooseBackground); backgroundControls.Children.Add(clearBackground); backgroundControls.Children.Add(backgroundStatus);

        appearanceContent.Children.Add(SettingsCard(
            "Appearance",
            "Theme and color choices",
            new UIElement[]
            {
                SettingLabel("COLOR MODE", muted),
                themeChoices,
                SettingLabel("ACCENT COLOR", muted),
                swatches,
                hex,
                new TextBlock
                {
                    Text = "Drag on the wheel to pick a color",
                    FontSize = 10,
                    Foreground = muted,
                    Margin = new Thickness(0, 6, 0, 3)
                },
                wheel,
                SettingLabel("INTERFACE LAYOUT", muted),
                densityChoices
            }, card, outline, ink, muted));

        startPageContent.Children.Add(SettingsCard(
            "Start page",
            "Choose what appears when you open a new tab",
            new UIElement[]
            {
                shortcutCheck,
                bookmarkCheck,
                suggestionCheck,
                SettingLabel("BACKGROUND IMAGE", muted),
                backgroundControls,
                SettingLabel("Homepage (blank uses Tryxtor start page)", muted),
                home
            }, card, outline, ink, muted));

        var savedLinksPanel = new StackPanel();

        void EditSavedLink(string currentTitle, string currentUrl, Action<string, string> save)
        {
            var editDialog = new Window
            {
                Title = "Edit saved website",
                Owner = dialog,
                Width = 440,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = bg,
                Foreground = ink,
                ResizeMode = ResizeMode.NoResize
            };

            var editPanel = new StackPanel { Margin = new Thickness(18) };

            editPanel.Children.Add(new TextBlock
            {
                Text = "Name",
                Foreground = ink,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var nameInput = new TextBox
            {
                Text = currentTitle,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 14),
                Background = field,
                Foreground = ink,
                BorderBrush = outline
            };
            editPanel.Children.Add(nameInput);

            editPanel.Children.Add(new TextBlock
            {
                Text = "URL",
                Foreground = ink,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var urlInput = new TextBox
            {
                Text = currentUrl,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 14),
                Background = field,
                Foreground = ink,
                BorderBrush = outline
            };
            editPanel.Children.Add(urlInput);

            var saveButton = new Button
            {
                Content = "Save",
                IsDefault = true,
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = accent,
                Foreground = light ? Brushes.White : Brushes.Black,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            saveButton.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(nameInput.Text) ||
                    string.IsNullOrWhiteSpace(urlInput.Text))
                {
                    MessageBox.Show(editDialog, "Both the name and URL are required.", "Tryxtor Settings",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                editDialog.DialogResult = true;
            };

            editPanel.Children.Add(saveButton);
            editDialog.Content = editPanel;

            if (editDialog.ShowDialog() == true)
                save(nameInput.Text.Trim(), Resolve(urlInput.Text.Trim()));
        }

        Border CreateLinkRow(string title, string url, Action edit, Action remove)
        {
            var row = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(0, 4, 0, 4)
            };

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            var editButton = new Button
            {
                Content = "Edit",
                Padding = new Thickness(9, 5, 9, 5),
                Margin = new Thickness(3),
                Background = field,
                Foreground = ink,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            editButton.Click += (_, _) => edit();

            var removeButton = new Button
            {
                Content = "Remove",
                Padding = new Thickness(9, 5, 9, 5),
                Margin = new Thickness(3),
                Background = field,
                Foreground = ink,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            removeButton.Click += (_, _) => remove();

            actions.Children.Add(editButton);
            actions.Children.Add(removeButton);
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);

            var details = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 5, 8, 5)
            };

            details.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = ink,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            });

            details.Children.Add(new TextBlock
            {
                Text = url,
                Foreground = muted,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            row.Children.Add(details);

            return new Border
            {
                Child = row,
                Background = card,
                BorderBrush = outline,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(5)
            };
        }

        void RenderSavedLinks()
        {
            savedLinksPanel.Children.Clear();

            savedLinksPanel.Children.Add(SettingLabel("BOOKMARKS", muted));

            if (preferences.Bookmarks.Count == 0)
            {
                savedLinksPanel.Children.Add(new TextBlock
                {
                    Text = "No bookmarks saved yet.",
                    Foreground = muted
                });
            }

            foreach (var bookmark in preferences.Bookmarks.ToList())
            {
                savedLinksPanel.Children.Add(CreateLinkRow(
                    bookmark.Title,
                    bookmark.Url,
                    () => EditSavedLink(bookmark.Title, bookmark.Url, (newTitle, newUrl) =>
                    {
                        bookmark.Title = newTitle;
                        bookmark.Url = newUrl;
                        SavePreferences();
                        RenderBookmarks();
                        UpdateBookmarkButton();
                        RenderSavedLinks();
                    }),
                    () =>
                    {
                        if (MessageBox.Show(dialog,
                            $"Remove bookmark '{bookmark.Title}'?",
                            "Remove bookmark",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) != MessageBoxResult.Yes)
                            return;

                        preferences.Bookmarks.Remove(bookmark);
                        SavePreferences();
                        RenderBookmarks();
                        UpdateBookmarkButton();
                        RenderSavedLinks();
                    }));
            }

            savedLinksPanel.Children.Add(SettingLabel("SHORTCUTS", muted));

            if (preferences.Shortcuts.Count == 0)
            {
                savedLinksPanel.Children.Add(new TextBlock
                {
                    Text = "No shortcuts saved yet.",
                    Foreground = muted
                });
            }

            foreach (var shortcut in preferences.Shortcuts.ToList())
            {
                savedLinksPanel.Children.Add(CreateLinkRow(
                    shortcut.Title,
                    shortcut.Url,
                    () => EditSavedLink(shortcut.Title, shortcut.Url, (newTitle, newUrl) =>
                    {
                        shortcut.Title = newTitle;
                        shortcut.Url = newUrl;
                        SavePreferences();
                        RenderShortcuts();
                        RenderSavedLinks();
                    }),
                    () =>
                    {
                        if (MessageBox.Show(dialog,
                            $"Remove shortcut '{shortcut.Title}'?",
                            "Remove shortcut",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) != MessageBoxResult.Yes)
                            return;

                        preferences.Shortcuts.Remove(shortcut);
                        SavePreferences();
                        RenderShortcuts();
                        RenderSavedLinks();
                    }));
            }
        }

        RenderSavedLinks();

        shortcutsContent.Children.Add(SettingsCard(
            "Shortcuts and bookmarks",
            "Manage your saved websites.",
            new UIElement[] { savedLinksPanel },
            card, outline, ink, muted));

        var vaultStatus = new TextBlock
        {
            Foreground = muted,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 10)
        };

        var masterPasswordBox = new PasswordBox
        {
            Padding = new Thickness(10),
            Background = field,
            Foreground = ink,
            BorderBrush = outline,
            BorderThickness = new Thickness(1)
        };

        var masterPasswordLabel = SettingLabel("MASTER PASSWORD", muted);
        var savedPasswordLabel = SettingLabel("SAVED PASSWORDS", muted);

        var passwordSearchBox = new TextBox
        {
            Height = 36,
            Padding = new Thickness(10, 6, 10, 6),
            FontSize = 14,
            Background = card,
            Foreground = ink,
            BorderBrush = outline
        };

        var passwordListPanel = new StackPanel
        {
            Orientation = Orientation.Vertical
        };

        var addPasswordButton = new Button
        {
            Content = "+ Add Password",
            Height = 38,
            Padding = new Thickness(14, 0, 14, 0),
            Background = accent,
            Foreground = light ? Brushes.White : Brushes.Black,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };

        var unlockButton = new Button
        {
            Content = passwordVault.VaultExists() ? "Unlock vault" : "Create vault",
            Padding = new Thickness(13, 9, 13, 9),
            Background = accent,
            Foreground = light ? Brushes.White : Brushes.Black,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var lockButton = new Button
        {
            Content = "Lock vault",
            Padding = new Thickness(13, 9, 13, 9),
            Background = field,
            Foreground = ink,
            BorderBrush = outline,
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed
        };

        passwordSearchBox.TextChanged += (_, _) => RefreshPasswordList();

        addPasswordButton.Click += (_, _) =>
        {
            if (!passwordVault.IsUnlocked)
                return;

            ShowAddPasswordDialog();
        };

        void UpdateVaultInterface()
        {
            if (passwordVault.IsUnlocked)
            {
                vaultStatus.Text = "Vault is unlocked.";
                unlockButton.IsEnabled = false;
                unlockButton.Visibility = Visibility.Collapsed;
                lockButton.Visibility = Visibility.Visible;
                masterPasswordBox.Visibility = Visibility.Collapsed;
                masterPasswordLabel.Visibility = Visibility.Collapsed;
                savedPasswordLabel.Visibility = Visibility.Visible;
                passwordSearchBox.Visibility = Visibility.Visible;
                addPasswordButton.Visibility = Visibility.Visible;
                RefreshPasswordList();
            }
            else
            {
                vaultStatus.Text = passwordVault.VaultExists()
                    ? "Vault is locked."
                    : "No vault created yet.";

                unlockButton.Content = passwordVault.VaultExists()
                    ? "Unlock vault"
                    : "Create vault";

                unlockButton.IsEnabled = true;
                unlockButton.Visibility = Visibility.Visible;
                lockButton.Visibility = Visibility.Collapsed;
                masterPasswordBox.Visibility = Visibility.Visible;
                masterPasswordLabel.Visibility = Visibility.Visible;
                savedPasswordLabel.Visibility = Visibility.Collapsed;
                passwordSearchBox.Visibility = Visibility.Collapsed;
                addPasswordButton.Visibility = Visibility.Collapsed;
                RefreshPasswordList();
            }
        }

        void RefreshPasswordList()
        {
            passwordListPanel.Children.Clear();

            if (!passwordVault.IsUnlocked)
                return;

            string search = passwordSearchBox.Text.Trim();

            var entries = passwordVault.GetEntries()
                .Where(e =>
                    e.Website.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    e.Username.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Website)
                .ToList();

            foreach (var entry in entries)
            {
                var entryPanel = new Border
                {
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 8),
                    Background = card,
                    BorderBrush = outline,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8)
                };

                var row = new DockPanel();

                var actions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var editButton = new Button
                {
                    Content = "Edit",
                    Padding = new Thickness(10, 6, 10, 6),
                    Background = field,
                    Foreground = ink,
                    BorderBrush = outline,
                    Margin = new Thickness(4, 0, 0, 0)
                };

                var deleteButton = new Button
                {
                    Content = "Delete",
                    Padding = new Thickness(10, 6, 10, 6),
                    Background = field,
                    Foreground = Brushes.IndianRed,
                    BorderBrush = outline,
                    Margin = new Thickness(4, 0, 0, 0)
                };

                editButton.Click += (_, _) => ShowEditPasswordDialog(entry);

                deleteButton.Click += (_, _) =>
                {
                    var result = MessageBox.Show(
                        $"Are you sure you want to delete the login for {entry.Website}?",
                        "Delete Password",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                        return;

                    try
                    {
                        passwordVault.DeleteEntry(entry.Id);
                        UpdateVaultInterface();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Could not delete password:\n\n" + ex.Message);
                    }
                };

                actions.Children.Add(editButton);
                actions.Children.Add(deleteButton);

                DockPanel.SetDock(actions, Dock.Right);
                row.Children.Add(actions);

                var details = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center
                };

                details.Children.Add(new TextBlock
                {
                    Text = entry.Website,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ink,
                    FontSize = 14
                });

                details.Children.Add(new TextBlock
                {
                    Text = entry.Username,
                    Foreground = muted,
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 0)
                });

                row.Children.Add(details);

                entryPanel.Child = row;
                passwordListPanel.Children.Add(entryPanel);
            }
        }

        void ShowAddPasswordDialog()
        {
            var entryDialog = new Window
            {
                Title = "Add Password",
                Owner = dialog,
                Width = 430,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = bg,
                Foreground = ink,
                ResizeMode = ResizeMode.NoResize
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(22)
            };

            TextBlock MakeLabel(string text)
            {
                return new TextBlock
                {
                    Text = text,
                    Foreground = muted,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 12, 0, 5)
                };
            }

            TextBox MakeInput()
            {
                return new TextBox
                {
                    Padding = new Thickness(10),
                    Background = field,
                    Foreground = ink,
                    BorderBrush = outline,
                    BorderThickness = new Thickness(1),
                    FontSize = 13
                };
            }

            var websiteBox = MakeInput();
            var usernameBox = MakeInput();

            var passwordBox = new PasswordBox
            {
                Padding = new Thickness(10),
                Background = field,
                Foreground = ink,
                BorderBrush = outline,
                BorderThickness = new Thickness(1),
                FontSize = 13
            };

            var notesBox = new TextBox
            {
                Padding = new Thickness(10),
                Background = field,
                Foreground = ink,
                BorderBrush = outline,
                BorderThickness = new Thickness(1),
                FontSize = 13,
                Height = 85,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Save a new login",
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                Foreground = ink
            });

            panel.Children.Add(MakeLabel("WEBSITE"));
            panel.Children.Add(websiteBox);

            panel.Children.Add(MakeLabel("USERNAME / EMAIL"));
            panel.Children.Add(usernameBox);

            panel.Children.Add(MakeLabel("PASSWORD"));
            panel.Children.Add(passwordBox);

            panel.Children.Add(MakeLabel("NOTES"));
            panel.Children.Add(notesBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(15, 8, 15, 8),
                Background = field,
                Foreground = ink,
                BorderBrush = outline,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var saveButton = new Button
            {
                Content = "Save Password",
                Padding = new Thickness(15, 8, 15, 8),
                Background = accent,
                Foreground = light ? Brushes.White : Brushes.Black,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                IsDefault = true
            };

            cancelButton.Click += (_, _) => entryDialog.Close();

            saveButton.Click += (_, _) =>
            {
                string website = websiteBox.Text.Trim();
                string username = usernameBox.Text.Trim();
                string password = passwordBox.Password;

                if (string.IsNullOrWhiteSpace(website) ||
                    string.IsNullOrWhiteSpace(username) ||
                    string.IsNullOrEmpty(password))
                {
                    MessageBox.Show(
                        "Website, username and password are required.",
                        "Password Manager",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Add HTTPS if the user didn't specify a scheme.
                if (!website.Contains("://"))
                    website = "https://" + website;

                if (!Uri.TryCreate(website, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps &&
                     uri.Scheme != Uri.UriSchemeHttp))
                {
                    MessageBox.Show(
                        "Please enter a valid website address.",
                        "Password Manager",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Store only the origin, not the page path or query.
                string origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');

                try
                {
                    passwordVault.AddEntry(new VaultEntry
                    {
                        Website = origin,
                        Username = username,
                        Password = password,
                        Notes = notesBox.Text.Trim()
                    });

                    UpdateVaultInterface();
                    entryDialog.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Could not save password:\n\n" + ex.Message,
                        "Password Manager",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            };

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(saveButton);
            panel.Children.Add(buttons);

            entryDialog.Content = panel;
            entryDialog.Loaded += (_, _) => websiteBox.Focus();
            entryDialog.ShowDialog();
        }

        void ShowEditPasswordDialog(VaultEntry entry)
        {
            var editDialog = new Window
            {
                Title = "Edit Password",
                Owner = dialog,
                Width = 430,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = bg,
                Foreground = ink,
                ResizeMode = ResizeMode.NoResize
            };

            var panel = new StackPanel { Margin = new Thickness(22) };

            TextBlock MakeLabel(string text)
            {
                return new TextBlock
                {
                    Text = text,
                    Foreground = muted,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 12, 0, 5)
                };
            }

            TextBox MakeInput(string value)
            {
                return new TextBox
                {
                    Text = value,
                    Padding = new Thickness(10),
                    Background = field,
                    Foreground = ink,
                    BorderBrush = outline,
                    BorderThickness = new Thickness(1),
                    FontSize = 13
                };
            }

            var websiteBox = MakeInput(entry.Website);
            var usernameBox = MakeInput(entry.Username);

            var passwordBox = new PasswordBox
            {
                Password = entry.Password,
                Padding = new Thickness(10),
                Background = field,
                Foreground = ink,
                BorderBrush = outline,
                BorderThickness = new Thickness(1),
                FontSize = 13
            };

            var notesBox = new TextBox
            {
                Text = entry.Notes,
                Padding = new Thickness(10),
                Background = field,
                Foreground = ink,
                BorderBrush = outline,
                BorderThickness = new Thickness(1),
                FontSize = 13,
                Height = 85,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Edit saved login",
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                Foreground = ink
            });

            panel.Children.Add(MakeLabel("WEBSITE"));
            panel.Children.Add(websiteBox);

            panel.Children.Add(MakeLabel("USERNAME / EMAIL"));
            panel.Children.Add(usernameBox);

            panel.Children.Add(MakeLabel("PASSWORD"));
            panel.Children.Add(passwordBox);

            panel.Children.Add(MakeLabel("NOTES"));
            panel.Children.Add(notesBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(15, 8, 15, 8),
                Background = field,
                Foreground = ink,
                BorderBrush = outline,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var saveButton = new Button
            {
                Content = "Save Changes",
                Padding = new Thickness(15, 8, 15, 8),
                Background = accent,
                Foreground = light ? Brushes.White : Brushes.Black,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                IsDefault = true
            };

            cancelButton.Click += (_, _) => editDialog.Close();

            saveButton.Click += (_, _) =>
            {
                string website = websiteBox.Text.Trim();
                string username = usernameBox.Text.Trim();
                string password = passwordBox.Password;

                if (string.IsNullOrWhiteSpace(website) ||
                    string.IsNullOrWhiteSpace(username) ||
                    string.IsNullOrEmpty(password))
                {
                    MessageBox.Show("Website, username and password are required.");
                    return;
                }

                if (!website.Contains("://"))
                    website = "https://" + website;

                if (!Uri.TryCreate(website, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps &&
                     uri.Scheme != Uri.UriSchemeHttp))
                {
                    MessageBox.Show("Please enter a valid website address.");
                    return;
                }

                string origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');

                try
                {
                    passwordVault.UpdateEntry(new VaultEntry
                    {
                        Id = entry.Id,
                        Website = origin,
                        Username = username,
                        Password = password,
                        Notes = notesBox.Text.Trim(),
                        CreatedAt = entry.CreatedAt
                    });

                    UpdateVaultInterface();
                    editDialog.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not update password:\n\n" + ex.Message);
                }
            };

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(saveButton);
            panel.Children.Add(buttons);

            editDialog.Content = panel;
            editDialog.ShowDialog();
        }

        unlockButton.Click += (_, _) =>
        {
            string password = masterPasswordBox.Password;

            try
            {
                if (passwordVault.VaultExists())
                {
                    passwordVault.Unlock(password);
                }
                else
                {
                    passwordVault.CreateVault(password);
                }

                masterPasswordBox.Clear();
                UpdateVaultInterface();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Password Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        };

        lockButton.Click += (_, _) =>
        {
            passwordVault.Lock();
            masterPasswordBox.Clear();
            UpdateVaultInterface();
        };

        UpdateVaultInterface();

        passwordContent.Children.Add(SettingsCard(
            "Password Manager",
            "Your passwords are encrypted and stored locally on this device.",
            new UIElement[]
            {
                vaultStatus,
                masterPasswordLabel,
                masterPasswordBox,
                unlockButton,
                lockButton,
                savedPasswordLabel,
                passwordSearchBox,
                addPasswordButton,
                passwordListPanel
            },
            card, outline, ink, muted));
        // Main settings area
        var settingsArea = new Grid
        {
            Margin = new Thickness(22, 0, 22, 10)
        };

        settingsArea.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(165) });

        settingsArea.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Sidebar
        var sidebar = new StackPanel
        {
            Margin = new Thickness(0, 0, 15, 0)
        };

        var pageHost = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        Grid.SetColumn(pageHost, 1);

        settingsArea.Children.Add(sidebar);
        settingsArea.Children.Add(pageHost);

        Button MakeCategoryButton(string title)
        {
            var button = new Button
            {
                Content = title,
                Padding = new Thickness(12, 11, 12, 11),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                Foreground = ink,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 4)
            };

            return button;
        }

        var appearanceButton = MakeCategoryButton("Appearance");
        var startPageButton = MakeCategoryButton("Start page");
        var shortcutsButton = MakeCategoryButton("Shortcuts & Bookmarks");
        var passwordButton = MakeCategoryButton("Password Manager");

        sidebar.Children.Add(appearanceButton);
        sidebar.Children.Add(startPageButton);
        sidebar.Children.Add(shortcutsButton);
        sidebar.Children.Add(new Border
        {
            Height = 1,
            Background = outline,
            Margin = new Thickness(5, 10, 5, 10)
        });
        sidebar.Children.Add(passwordButton);

        void ShowCategory(string category)
        {
            settingsCategory = category;
            pageHost.Content = category switch
            {
                "Start page" => startPageContent,
                "Shortcuts" => shortcutsContent,
                "Password Manager" => passwordContent,
                _ => appearanceContent
            };

            foreach (var button in new[]
            {
        appearanceButton,
        startPageButton,
        shortcutsButton,
        passwordButton
    })
            {
                bool selected = button.Tag?.ToString() == category;

                button.Background = selected
                    ? accent
                    : Brushes.Transparent;

                button.Foreground = selected
                    ? (light ? Brushes.White : Brushes.Black)
                    : ink;

                button.FontWeight = selected
                    ? FontWeights.SemiBold
                    : FontWeights.Normal;
            }
        }

        appearanceButton.Tag = "Appearance";
        startPageButton.Tag = "Start page";
        shortcutsButton.Tag = "Shortcuts";
        passwordButton.Tag = "Password Manager";

        appearanceButton.Click += (_, _) => ShowCategory("Appearance");
        startPageButton.Click += (_, _) => ShowCategory("Start page");
        shortcutsButton.Click += (_, _) => ShowCategory("Shortcuts");
        passwordButton.Click += (_, _) => ShowCategory("Password Manager");

        ShowCategory(settingsCategory);

        Grid.SetRow(settingsArea, 1);
        layout.Children.Add(settingsArea);

        var footer = new DockPanel { Margin = new Thickness(22, 12, 22, 18), LastChildFill = false };
        var savedStatus = new TextBlock { Text = "", Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 9, 14, 9), Foreground = ink, Background = field, BorderBrush = outline, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => dialog.Close();
        var save = new Button { Content = "Save changes", Padding = new Thickness(17, 9, 17, 9), Foreground = light ? Brushes.White : new SolidColorBrush(Color.FromRgb(23, 29, 18)), Background = accent, BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold };
        save.Click += (_, _) =>
        {
            if (!IsColor(hex.Text)) { MessageBox.Show("Enter a valid color such as #83C9FF.", "Settings"); return; }
            preferences.ColorTheme = selectedColorTheme;

            preferences.Theme = new[]
            {
                "Light", "Snow", "Mint", "Lavender", "Rose", "Sand", "Sky", "Slate"
            }.Contains(selectedColorTheme) ? "Light" : "Dark";
            preferences.Density = selectedDensity;
            preferences.Accent = hex.Text.Trim();
            preferences.ShowBookmarks = bookmarkCheck.IsChecked == true;
            preferences.ShowShortcuts = shortcutCheck.IsChecked == true;
            preferences.ShowSuggestions = suggestionCheck.IsChecked == true;
            preferences.HomePage = home.Text.Trim();
            if (!string.IsNullOrWhiteSpace(selectedBackgroundPath))
            {
                if (!System.IO.File.Exists(selectedBackgroundPath)) { MessageBox.Show("The selected background image could not be found.", "Settings"); return; }
                try
                {
                    string extension = System.IO.Path.GetExtension(selectedBackgroundPath);
                    string destination = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(settingsPath)!, "start-page-background" + extension);
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(settingsPath)!);
                    if (!string.Equals(System.IO.Path.GetFullPath(selectedBackgroundPath), System.IO.Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        System.IO.File.Copy(selectedBackgroundPath, destination, true);
                    preferences.StartPageBackground = destination;
                }
                catch (Exception ex) { MessageBox.Show("Could not save the background image: " + ex.Message, "Settings"); return; }
            }
            else preferences.StartPageBackground = "";
            SavePreferences(); ApplyAppearance(); RenderBookmarks(); RenderShortcuts();
            // The dialog's colors are captured when it opens, so reopen it to pick up the new accent/theme.
            dialog.Close();
            Dispatcher.BeginInvoke(() => Settings_Click(this, new RoutedEventArgs()));
        };
        footer.Children.Add(savedStatus); footer.Children.Add(cancel); footer.Children.Add(save); Grid.SetRow(footer, 2); layout.Children.Add(footer);
        dialog.Content = layout;
        dialog.ShowDialog();
    }

    static TextBlock SettingLabel(string text, Brush muted) => new() { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = muted, Margin = new Thickness(0, 9, 0, 5) };

    Button MakeDensityChoice(string title, bool selected, Brush ink, Brush muted, Brush outline)
    {
        var preview = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        preview.Children.Add(new Border { Height = title == "Compact" ? 5 : title == "Balanced" ? 8 : 11, Background = ParseBrush(preferences.Accent, Brushes.LightGreen), CornerRadius = new CornerRadius(3), Margin = new Thickness(3, 0, 3, 3) });
        preview.Children.Add(new Border { Height = title == "Compact" ? 7 : title == "Balanced" ? 11 : 15, Background = new SolidColorBrush(preferences.Theme == "Light" ? Color.FromRgb(222, 228, 236) : Color.FromRgb(58, 67, 81)), CornerRadius = new CornerRadius(3), Margin = new Thickness(3, 0, 3, 0) });
        var sample = new Border { Height = 36, Background = new SolidColorBrush(preferences.Theme == "Light" ? Color.FromRgb(246, 248, 251) : Color.FromRgb(19, 23, 30)), CornerRadius = new CornerRadius(6), Padding = new Thickness(5), Child = preview };
        var stack = new StackPanel();
        stack.Children.Add(sample);
        stack.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 7, 0, 1) });
        stack.Children.Add(new TextBlock { Text = title == "Compact" ? "More space" : title == "Balanced" ? "Everyday" : "Room to breathe", FontSize = 9, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Center });
        var frame = new Border { Child = stack, Background = new SolidColorBrush(preferences.Theme == "Light" ? Colors.White : Color.FromRgb(27, 32, 41)), BorderBrush = selected ? ParseBrush(preferences.Accent, Brushes.LightGreen) : outline, BorderThickness = new Thickness(selected ? 2 : 1), CornerRadius = new CornerRadius(10), Padding = new Thickness(7) };
        return new Button { Content = frame, Tag = frame, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Cursor = Cursors.Hand, ToolTip = title + " interface layout" };
    }

    static void SetDensityChoiceState(Button button, bool selected, Brush accent, Brush outline)
    {
        if (button.Tag is Border frame)
        {
            frame.BorderBrush = selected ? accent : outline;
            frame.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    static Border SettingsCard(string title, string description, IEnumerable<UIElement> controls, Brush background, Brush outline, Brush text, Brush muted)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = text, Margin = new Thickness(0, 0, 0, 3) });
        stack.Children.Add(new TextBlock { Text = description, FontSize = 11, Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 11) });
        foreach (var item in controls) { if (item is FrameworkElement element) element.Margin = new Thickness(0, 0, 0, 5); stack.Children.Add(item); }
        return new Border { Background = background, BorderBrush = outline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 12), Child = stack };
    }

    static bool IsColor(string value) { try { _ = ColorConverter.ConvertFromString(value); return true; } catch { return false; } }
}
