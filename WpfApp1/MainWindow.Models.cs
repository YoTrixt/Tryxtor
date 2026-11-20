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
    sealed class BrowserTab
    {
        public string Title = "New tab";
        public string Url = "";
        public WebView2 View = new();
        public MainWindow Owner = null!;
        public Button TitleButton = null!;
        public Border Surface = null!;
        public string? PendingLoginOrigin;
        public string? PendingLoginUsername;
        public string? PendingLoginPassword;
        public int LoginAttemptId;
    }

    sealed class Shortcut
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
    }

    sealed class Bookmark
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
    }

    sealed class RecentPage
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
    }

    sealed class AddressSuggestion
    {
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Value { get; set; } = "";
        public bool IsRecentSearch { get; set; }
    }

    sealed class Preferences
    {
        public string Accent { get; set; } = "#B6F36A";
        public string Theme { get; set; } = "Dark";
        public string ColorTheme { get; set; } = "Midnight";
        public string Density { get; set; } = "Balanced";
        public string HomePage { get; set; } = "";
        public string StartPageBackground { get; set; } = "";
        public string SearchEngine { get; set; } = "Google";
        public bool ShowBookmarks { get; set; } = true;
        public bool ShowShortcuts { get; set; } = true;
        public bool ShowSuggestions { get; set; } = true;
        public List<string> RecentSearches { get; set; } = new();
        public List<RecentPage> RecentPages { get; set; } = new();
        public List<Bookmark> Bookmarks { get; set; } = new()
        {
            new() { Title = "YouTube", Url = "https://youtube.com" }
        };
        public List<Shortcut> Shortcuts { get; set; } = new()
        {
            new() { Title = "YouTube", Url = "https://youtube.com" },
        };
    }

    sealed class BrowserTheme
    {
        public string Name { get; }
        public string Background { get; }
        public string Surface { get; }
        public string Inset { get; }
        public string Outline { get; }
        public string Text { get; }
        public string Muted { get; }

        public BrowserTheme(
            string name,
            string background,
            string surface,
            string inset,
            string outline,
            string text,
            string muted)
        {
            Name = name;
            Background = background;
            Surface = surface;
            Inset = inset;
            Outline = outline;
            Text = text;
            Muted = muted;
        }
    }

    static readonly Dictionary<string, BrowserTheme> BuiltInThemes = new()
    {
        // Dark themes
        ["Midnight"] = new("Midnight", "#101319", "#171A21", "#1E242E", "#37404D", "#EEF0F4", "#9298A5"),
        ["OLED Black"] = new("OLED Black", "#000000", "#0A0A0A", "#141414", "#303030", "#F5F5F5", "#999999"),
        ["Ocean"] = new("Ocean", "#142B3A", "#1B3545", "#234253", "#36596B", "#E4F3FA", "#9BBBCB"),
        ["Cyberpunk"] = new("Cyberpunk", "#181326", "#211A32", "#2B2140", "#49345D", "#F5EAF8", "#B9A3C7"),
        ["Crimson"] = new("Crimson", "#281719", "#351F22", "#42272B", "#624044", "#F8E9E9", "#C6A4A5"),
        ["Arctic"] = new("Arctic", "#172533", "#203342", "#293F50", "#405B6D", "#EAF5FC", "#A4BDCE"),
        ["Emerald"] = new("Emerald", "#142820", "#1C3429", "#254334", "#3C604B", "#E6F5EC", "#A1C2AD"),
        ["Graphite"] = new("Graphite", "#222222", "#2B2B2B", "#353535", "#484848", "#F3F3F3", "#AAAAAA"),
        ["Royal"] = new("Royal", "#211B35", "#2B2343", "#372C53", "#51416F", "#F0EAFE", "#B9B1D0"),
        ["Coffee"] = new("Coffee", "#30251F", "#3A2D25", "#49382D", "#665040", "#F4E8DD", "#C7AFA3"),
        ["Cherry"] = new("Cherry", "#301B29", "#3B2333", "#492B3E", "#69445A", "#F9EAF2", "#C8A5B7"),
        ["Nord"] = new("Nord", "#2E3440", "#3B4252", "#434C5E", "#596477", "#ECEFF4", "#B5BEC9"),
        ["Volcano"] = new("Volcano", "#301D18", "#3D251E", "#4B2F25", "#704637", "#F8E9DD", "#C7A18D"),
        ["Sakura"] = new("Sakura", "#33232E", "#402B38", "#503545", "#704D60", "#FCEBF2", "#C9A8B8"),
        ["Forest"] = new("Forest", "#202B28", "#293832", "#34453D", "#4A6255", "#E4EEE8", "#A6B9AE"),
        ["Sunset"] = new("Sunset", "#30231F", "#3D2C26", "#4B3830", "#6B5044", "#F5E9E2", "#C7AFA3"),

        // Light themes
        ["Light"] = new("Light", "#F4F6F9", "#FFFFFF", "#FFFFFF", "#CAD2DD", "#23272F", "#696F7B"),
        ["Snow"] = new("Snow", "#F4F6FA", "#FFFFFF", "#EAEDF3", "#D5DAE4", "#252B36", "#737B89"),
        ["Mint"] = new("Mint", "#EAF4EF", "#F8FFFA", "#DCECE3", "#C3D9CC", "#203B30", "#648071"),
        ["Lavender"] = new("Lavender", "#F0EDF8", "#FBF9FF", "#E5DFF1", "#D1C7E3", "#302A42", "#776D8C"),
        ["Rose"] = new("Rose", "#F9EEF1", "#FFF9FB", "#F1DFE5", "#E5C8D2", "#412B35", "#896B78"),
        ["Sand"] = new("Sand", "#F3EBDD", "#FFFAF1", "#EAE0CE", "#D9C9AF", "#3B3024", "#85735E"),
        ["Sky"] = new("Sky", "#EAF3FA", "#F8FCFF", "#DCEAF5", "#C5D9E8", "#25394A", "#647D91"),
        ["Slate"] = new("Slate", "#E9EDF2", "#F8FAFC", "#DCE2EA", "#C5CEDA", "#293442", "#687587")
    };
}
