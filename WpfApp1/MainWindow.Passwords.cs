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
    private VaultService passwordVault = new();

    private Popup? passwordAccountPopup;

    private Window? passwordSavePopup;

    void ShowPasswordAccountMenu(BrowserTab tab, List<VaultEntry> accounts)
    {
        if (tab != tab.Owner.activeTab || !tab.View.IsVisible)
            return;

        if (tab.Owner.privateMode)
            return;

        if (tab.Owner.passwordAccountPopup?.IsOpen == true)
            return;

        bool light = tab.Owner.preferences.Theme == "Light";
        bool isLocked = !tab.Owner.passwordVault.IsUnlocked;

        var background = new SolidColorBrush(
            light ? Color.FromRgb(255, 255, 255) : Color.FromRgb(30, 34, 43));
        var foreground = new SolidColorBrush(
            light ? Color.FromRgb(35, 42, 53) : Color.FromRgb(239, 242, 247));
        var muted = new SolidColorBrush(
            light ? Color.FromRgb(110, 120, 135) : Color.FromRgb(155, 165, 180));
        var accent = ParseBrush(tab.Owner.preferences.Accent, Brushes.LightGreen);

        var border = new Border
        {
            Background = background,
            BorderBrush = new SolidColorBrush(
                light ? Color.FromRgb(220, 225, 233) : Color.FromRgb(57, 65, 78)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            Width = 290,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.25
            }
        };

        var panel = new StackPanel();
        border.Child = panel;

        panel.Children.Add(new TextBlock
        {
            Text = "TRYXTOR PASSWORDS",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = muted,
            Margin = new Thickness(10, 7, 10, 10)
        });

        var popup = new Popup
        {
            Child = border,
            PlacementTarget = tab.View,
            Placement = PlacementMode.MousePoint,
            AllowsTransparency = true,
            StaysOpen = true
        };
        tab.Owner.passwordAccountPopup = popup;
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(tab.Owner.passwordAccountPopup, popup))
                tab.Owner.passwordAccountPopup = null;
        };

        if (isLocked)
        {
            panel.Children.Add(new TextBlock
            {
                Text = tab.Owner.passwordVault.VaultExists()
                    ? "Your password vault is locked."
                    : "Create a vault to start saving passwords.",
                FontSize = 12,
                Foreground = foreground,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 2, 10, 10)
            });

            var unlockButton = new Button
            {
                Content = tab.Owner.passwordVault.VaultExists()
                    ? "Unlock vault"
                    : "Create vault",
                Background = accent,
                Foreground = light ? Brushes.White : Brushes.Black,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(8, 0, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.SemiBold
            };

            unlockButton.Click += (_, _) =>
            {
                popup.IsOpen = false;

                if (tab.Owner.PromptToUnlockVault())
                {
                    var refreshedAccounts =
                        tab.Owner.passwordVault.GetEntriesForWebsite(tab.Url);

                    tab.Owner.ShowPasswordAccountMenu(tab, refreshedAccounts);
                }
            };

            panel.Children.Add(unlockButton);
        }
        else if (accounts.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No saved accounts for this website.",
                FontSize = 12,
                Foreground = muted,
                Margin = new Thickness(10, 2, 10, 10)
            });
        }

        foreach (var account in accounts)
        {
            var button = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 9, 10, 9),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand
            };

            var accountPanel = new StackPanel();
            accountPanel.Children.Add(new TextBlock
            {
                Text = account.Username,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground
            });
            accountPanel.Children.Add(new TextBlock
            {
                Text = account.Website,
                FontSize = 10,
                Foreground = muted,
                Margin = new Thickness(0, 3, 0, 0)
            });
            button.Content = accountPanel;

            button.MouseEnter += (_, _) =>
                button.Background = new SolidColorBrush(
                    light ? Color.FromRgb(238, 243, 249) : Color.FromRgb(48, 56, 69));
            button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;

            button.Click += async (_, _) =>
            {
                popup.IsOpen = false;

                if (!tab.Owner.passwordVault.IsUnlocked ||
                    !Uri.TryCreate(tab.Url, UriKind.Absolute, out var currentUri))
                    return;

                string currentOrigin = currentUri.GetLeftPart(UriPartial.Authority);
                if (!string.Equals(
                    currentOrigin,
                    account.Website.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
                    return;

                string usernameJson = JsonSerializer.Serialize(account.Username);
                string passwordJson = JsonSerializer.Serialize(account.Password);

                string script = $$"""
                (() => {
                    const password = document.querySelector('input[type="password"]');
                    if (!password) return;

                    const form = password.form || document;
                    const username = Array.from(form.querySelectorAll('input')).find(input =>
                        input !== password &&
                        (input.autocomplete === 'username' ||
                         input.type === 'email' ||
                         input.type === 'text'));

                    function fill(input, value) {
                        const setter = Object.getOwnPropertyDescriptor(
                            HTMLInputElement.prototype, 'value').set;
                        setter.call(input, value);
                        input.dispatchEvent(new Event('input', { bubbles: true }));
                        input.dispatchEvent(new Event('change', { bubbles: true }));
                    }

                    if (username) fill(username, {{usernameJson}});
                    fill(password, {{passwordJson}});
                })();
                """;

                await tab.View.CoreWebView2.ExecuteScriptAsync(script);
            };

            panel.Children.Add(button);
        }

        popup.IsOpen = true;
    }

    async Task OfferSaveAfterSuccessfulLogin(BrowserTab tab, int attempt)
    {
        // A visible password field after submitting means the login form is still
        // showing (wrong password, error message, etc.), so treat it as a failure.
        const string loginFormVisibleScript = """
            Array.from(document.querySelectorAll('input[type="password"]')).some(input => {
                const rect = input.getBoundingClientRect();
                const style = getComputedStyle(input);
                return rect.width > 0 && rect.height > 0 &&
                       style.visibility !== 'hidden' && style.display !== 'none';
            })
            """;

        foreach (int delayMs in new[] { 1200, 1800, 3000 })
        {
            await Task.Delay(delayMs);

            if (tab.LoginAttemptId != attempt ||
                tab.PendingLoginOrigin == null ||
                tab.View.CoreWebView2 == null)
                return;

            bool loginFormStillShown;
            try
            {
                string result = await tab.View.CoreWebView2.ExecuteScriptAsync(loginFormVisibleScript);
                loginFormStillShown = result == "true";
            }
            catch (Exception)
            {
                return;
            }

            if (tab.LoginAttemptId != attempt || !passwordVault.IsUnlocked)
                return;

            if (loginFormStillShown)
                continue;

            var matchingAccount = passwordVault
                .GetEntriesForWebsite(tab.PendingLoginOrigin)
                .FirstOrDefault(account => string.Equals(
                    account.Username,
                    tab.PendingLoginUsername,
                    StringComparison.OrdinalIgnoreCase));

            if (matchingAccount == null || matchingAccount.Password != tab.PendingLoginPassword)
                ShowPasswordSavePopup(tab, matchingAccount);

            return;
        }

        // The login form never went away: the login most likely failed.
        if (tab.LoginAttemptId == attempt)
        {
            tab.PendingLoginOrigin = null;
            tab.PendingLoginUsername = null;
            tab.PendingLoginPassword = null;
        }
    }

    void ShowPasswordSavePopup(BrowserTab tab, VaultEntry? existingAccount)
    {
        if (tab.Owner.privateMode ||
            tab != tab.Owner.activeTab ||
            !tab.View.IsVisible ||
            !tab.Owner.passwordVault.IsUnlocked ||
            string.IsNullOrWhiteSpace(tab.PendingLoginOrigin) ||
            string.IsNullOrWhiteSpace(tab.PendingLoginUsername) ||
            string.IsNullOrEmpty(tab.PendingLoginPassword))
            return;

        tab.Owner.ClosePasswordSavePopup();

        string website = tab.PendingLoginOrigin;
        string username = tab.PendingLoginUsername;
        string password = tab.PendingLoginPassword;

        bool isUpdate = existingAccount != null;
        bool light = tab.Owner.preferences.Theme == "Light";

        Brush background = new SolidColorBrush(
            light ? Color.FromRgb(255, 255, 255) : Color.FromRgb(30, 34, 43));

        Brush foreground = new SolidColorBrush(
            light ? Color.FromRgb(35, 42, 53) : Color.FromRgb(239, 242, 247));

        Brush muted = new SolidColorBrush(
            light ? Color.FromRgb(110, 120, 135) : Color.FromRgb(155, 165, 180));

        Brush accent = ParseBrush(tab.Owner.preferences.Accent, Brushes.LightGreen);

        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text = isUpdate ? "UPDATE PASSWORD?" : "SAVE PASSWORD?",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = muted,
            Margin = new Thickness(0, 0, 0, 10)
        });

        panel.Children.Add(new TextBlock
        {
            Text = isUpdate
                ? $"The password for {username} has changed."
                : $"Save the login for {username}?",
            FontSize = 14,
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new TextBlock
        {
            Text = website,
            FontSize = 11,
            Foreground = muted,
            Margin = new Thickness(0, 7, 0, 18)
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var notNowButton = new Button
        {
            Content = "Not now",
            Padding = new Thickness(13, 8, 13, 8),
            Margin = new Thickness(0, 0, 8, 0),
            Background = Brushes.Transparent,
            Foreground = foreground,
            BorderBrush = muted,
            Cursor = Cursors.Hand
        };

        var saveButton = new Button
        {
            Content = isUpdate ? "Update" : "Save",
            Padding = new Thickness(16, 8, 16, 8),
            Background = accent,
            Foreground = light ? Brushes.White : Brushes.Black,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand
        };

        buttons.Children.Add(notNowButton);
        buttons.Children.Add(saveButton);
        panel.Children.Add(buttons);

        var saveWindow = new Window
        {
            Title = "Tryxtor Passwords",
            Owner = tab.Owner,
            Width = 370,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Brushes.Transparent,
            Foreground = foreground,
            Content = new Border
            {
                Background = background,
                BorderBrush = new SolidColorBrush(
                    light ? Color.FromRgb(220, 225, 233) : Color.FromRgb(57, 65, 78)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = panel
            }
        };

        tab.Owner.passwordSavePopup = saveWindow;

        void ClearPendingLogin()
        {
            tab.PendingLoginOrigin = null;
            tab.PendingLoginUsername = null;
            tab.PendingLoginPassword = null;
        }

        saveWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(tab.Owner.passwordSavePopup, saveWindow))
                tab.Owner.passwordSavePopup = null;

            ClearPendingLogin();
        };

        notNowButton.Click += (_, _) => saveWindow.Close();

        saveButton.Click += (_, _) =>
        {
            try
            {
                if (isUpdate && existingAccount != null)
                {
                    tab.Owner.passwordVault.UpdateEntry(new VaultEntry
                    {
                        Id = existingAccount.Id,
                        Website = website,
                        Username = username,
                        Password = password,
                        Notes = existingAccount.Notes
                    });
                }
                else
                {
                    tab.Owner.passwordVault.AddEntry(new VaultEntry
                    {
                        Website = website,
                        Username = username,
                        Password = password
                    });
                }

                saveWindow.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not save the login:\n\n" + ex.Message,
                    "Tryxtor Passwords",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };
        saveWindow.Show();
    }

    void ClosePasswordSavePopup()
    {
        if (passwordSavePopup == null)
            return;

        var popup = passwordSavePopup;
        passwordSavePopup = null;

        popup.Close();
    }

    bool PromptToUnlockVault()
    {
        bool light = preferences.Theme == "Light";
        var dialog = new Window
        {
            Title = passwordVault.VaultExists() ? "Unlock Tryxtor" : "Create Tryxtor vault",
            Owner = this,
            Width = 390,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(
                light ? Color.FromRgb(248, 249, 251) : Color.FromRgb(23, 26, 33)),
            Foreground = new SolidColorBrush(
                light ? Color.FromRgb(35, 42, 53) : Color.FromRgb(239, 242, 247))
        };

        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = passwordVault.VaultExists()
                ? "Enter your master password to access saved accounts."
                : "Choose a master password to create your encrypted vault.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var passwordBox = new PasswordBox
        {
            Padding = new Thickness(10),
            Background = new SolidColorBrush(
                light ? Colors.White : Color.FromRgb(30, 36, 46)),
            Foreground = dialog.Foreground,
            BorderBrush = new SolidColorBrush(
                light ? Color.FromRgb(202, 210, 221) : Color.FromRgb(55, 64, 77))
        };
        panel.Children.Add(passwordBox);

        var errorText = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        panel.Children.Add(errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        var submitButton = new Button
        {
            Content = passwordVault.VaultExists() ? "Unlock" : "Create vault",
            Padding = new Thickness(14, 8, 14, 8),
            Background = ParseBrush(preferences.Accent, Brushes.LightGreen),
            Foreground = light ? Brushes.White : Brushes.Black,
            BorderThickness = new Thickness(0),
            IsDefault = true
        };

        submitButton.Click += (_, _) =>
        {
            try
            {
                string masterPassword = passwordBox.Password;
                if (passwordVault.VaultExists())
                    passwordVault.Unlock(masterPassword);
                else
                    passwordVault.CreateVault(masterPassword);

                passwordBox.Clear();
                dialog.DialogResult = true;
            }
            catch (Exception ex)
            {
                errorText.Text = ex.Message;
                passwordBox.Clear();
                passwordBox.Focus();
            }
        };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(submitButton);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => passwordBox.Focus();

        bool? result = dialog.ShowDialog();
        passwordBox.Clear();
        return result == true && passwordVault.IsUnlocked;
    }
}
