using System.Runtime.InteropServices;
using Microsoft.Win32;
using Ferry.Core;

namespace Ferry;

/// <summary>Light and dark color sets, and helpers that apply them to windows and standard controls.</summary>
sealed class Theme
{
    public required bool IsDark { get; init; }
    public required Color Background { get; init; }   // menu bar, toolbar, status bar, dialogs
    public required Color Sidebar { get; init; }
    public required Color Content { get; init; }      // download list, details, text boxes, menus
    public required Color Border { get; init; }
    public required Color Text { get; init; }
    public required Color TextMuted { get; init; }
    public required Color Accent { get; init; }
    public required Color Selection { get; init; }
    public required Color Hover { get; init; }
    public required Color RowHover { get; init; }
    public required Color Track { get; init; }        // empty part of progress bars
    public required Color Green { get; init; }
    public required Color Amber { get; init; }
    public required Color Red { get; init; }
    public required Color Purple { get; init; }
    public required Color Teal { get; init; }
    public required Color Pink { get; init; }

    public static readonly Theme Light = new()
    {
        IsDark = false,
        Background = Hex(0xF6F7F9), Sidebar = Hex(0xEEF1F5), Content = Hex(0xFFFFFF), Border = Hex(0xE1E4EA),
        Text = Hex(0x1D2128), TextMuted = Hex(0x6B7280), Accent = Hex(0x2563EB),
        Selection = Hex(0xDCE7FD), Hover = Hex(0xE5E9F0), RowHover = Hex(0xF3F6FB), Track = Hex(0xE5E7EB),
        Green = Hex(0x16A34A), Amber = Hex(0xD97706), Red = Hex(0xDC2626), Purple = Hex(0x7C3AED), Teal = Hex(0x0D9488), Pink = Hex(0xDB2777),
    };

    public static readonly Theme Dark = new()
    {
        IsDark = true,
        Background = Hex(0x1C1D20), Sidebar = Hex(0x17181B), Content = Hex(0x212226), Border = Hex(0x303238),
        Text = Hex(0xE7E8EA), TextMuted = Hex(0x9BA1A9), Accent = Hex(0x4C8DFF),
        Selection = Hex(0x243A60), Hover = Hex(0x2C2E34), RowHover = Hex(0x27292E), Track = Hex(0x36393F),
        Green = Hex(0x22C55E), Amber = Hex(0xF59E0B), Red = Hex(0xF87171), Purple = Hex(0xA78BFA), Teal = Hex(0x2DD4BF), Pink = Hex(0xF472B6),
    };

    public static Theme Current { get; private set; } = Light;

    /// <summary>Picks the colors for the "System", "Light" or "Dark" setting.</summary>
    public static void Use(string setting) =>
        Current = setting switch { "Dark" => Dark, "Light" => Light, _ => WindowsUsesDarkMode() ? Dark : Light };

    public static bool WindowsUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (System.Security.SecurityException) { return false; }
    }

    public Color StatusColor(DownloadStatus status) => status switch
    {
        DownloadStatus.Downloading => Accent,
        DownloadStatus.Merging => Purple,
        DownloadStatus.Completed => Green,
        DownloadStatus.Error => Red,
        _ => Amber,
    };

    /// <summary>Dark or light title bar (Windows 10 1809+); on Windows 11 also colors it like the window.</summary>
    public void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int caption = ColorTranslator.ToWin32(Background);
        DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        int text = ColorTranslator.ToWin32(Text);
        DwmSetWindowAttribute(form.Handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }

    /// <summary>Colors a dialog and its standard controls. Buttons tagged "primary" get the accent color.</summary>
    public void Apply(Control root)
    {
        root.BackColor = Background;
        root.ForeColor = Text;
        foreach (Control child in root.Controls) ApplyTo(child);
    }

    void ApplyTo(Control c)
    {
        switch (c)
        {
            case TextBox or NumericUpDown or ComboBox:
                c.BackColor = Content;
                c.ForeColor = Text;
                if (c is ComboBox combo) combo.FlatStyle = FlatStyle.Flat;
                break;
            case Button b:
                bool primary = Equals(b.Tag, "primary");
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = primary ? Accent : Content;
                b.ForeColor = primary ? Color.White : Text;
                b.FlatAppearance.BorderColor = primary ? Accent : Border;
                b.FlatAppearance.MouseOverBackColor = primary ? Mix(Accent, Color.Black, 0.12f) : Hover;
                b.FlatAppearance.MouseDownBackColor = primary ? Mix(Accent, Color.Black, 0.22f) : Selection;
                break;
            case RadioButton { Appearance: Appearance.Button } r:
                // Segmented choices and the settings navigation.
                r.FlatStyle = FlatStyle.Flat;
                r.ForeColor = Text;
                r.FlatAppearance.BorderSize = Equals(r.Tag, "segment") ? 1 : 0;
                r.FlatAppearance.BorderColor = Border;
                r.FlatAppearance.CheckedBackColor = Selection;
                r.FlatAppearance.MouseOverBackColor = Hover;
                r.FlatAppearance.MouseDownBackColor = Selection;
                break;
            case LinkLabel link:
                link.LinkColor = link.ActiveLinkColor = Accent;
                break;
            case Label or CheckBox or RadioButton:
                c.ForeColor = Equals(c.Tag, "muted") ? TextMuted : Text;
                break;
        }
        foreach (Control child in c.Controls) ApplyTo(child);
    }

    public static Color Mix(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));

    static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_CAPTION_COLOR = 35;
    const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Switches a control to one of Windows' visual styles, e.g. "DarkMode_CFD" for dark scroll bars.</summary>
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    public static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);
}
