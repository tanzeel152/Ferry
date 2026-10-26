using System.Text.Json;
using OSDM.Core;

namespace OSDM;

/// <summary>Settings window with General, Connections, Browser and Video pages. <see cref="Result"/> holds the new settings after OK.</summary>
sealed class SettingsForm : Form
{
    static readonly (string Label, string Format)[] VideoQualities = VideoInfo.Presets;
    static readonly string[] Themes = { "System", "Light", "Dark" };

    readonly Panel _pages = new() { Dock = DockStyle.Fill };
    readonly FlowLayoutPanel _nav = new() { Dock = DockStyle.Left, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    readonly List<string> _qualityFormats = VideoQualities.Select(q => q.Format).ToList();

    // General
    readonly ComboBox _theme = List("Same as Windows", "Light", "Dark");
    readonly TextBox _folder = new() { Dock = DockStyle.Fill };
    readonly CheckBox _categories = Check("Sort downloads into folders by type");
    readonly CheckBox _startWithWindows = Check("Start OSDM when Windows starts");
    // Connections
    readonly NumericUpDown _connections = Number(1, 32);
    readonly NumericUpDown _simultaneous = Number(1, 20);
    readonly CheckBox _limitSpeed = Check("Limit to");
    readonly NumericUpDown _speedLimit = Number(1, 10_000_000);
    readonly NumericUpDown _retries = Number(0, 50);
    readonly TextBox _proxy = new() { Dock = DockStyle.Fill, PlaceholderText = "Use the Windows proxy settings" };
    // Browser
    readonly CheckBox _confirm = Check("Ask before starting downloads sent by the browser");
    readonly CheckBox _clipboard = Check("Offer to download links I copy");
    readonly TextBox _types = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right, Multiline = true, ScrollBars = ScrollBars.Vertical };
    readonly NumericUpDown _port = Number(1024, 65535);
    // Video
    readonly ComboBox _quality = List(VideoQualities.Select(q => q.Label).ToArray());
    readonly ComboBox _container = List("mp4", "mkv", "webm");
    readonly Label _toolsStatus = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    readonly Button _installTools = new() { Text = "Install", AutoSize = true };

    readonly Settings _current;
    readonly Func<Task> _installVideoTools;
    readonly Font _headingFont;

    public Settings Result { get; private set; }
    public bool StartWithWindowsChecked => _startWithWindows.Checked;

    public SettingsForm(Settings current, bool startWithWindows, Func<Task> installVideoTools, Action<IWin32Window?> showExtensionHelp)
    {
        _current = current;
        _installVideoTools = installVideoTools;
        Result = current;
        _headingFont = new Font(Font.FontFamily, Font.Size * 1.45f, FontStyle.Bold);
        Text = "Settings";
        Icon = MainForm.LoadAppIcon();
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(S(760), S(540));

        foreach (var n in new[] { _connections, _simultaneous, _retries, _port }) n.Width = S(100);
        _speedLimit.Width = S(110);
        _speedLimit.ThousandsSeparator = true;
        _theme.Width = _quality.Width = _container.Width = S(220);
        _types.Height = S(84);

        // Pages
        var general = Page("General");
        Row(general, "Appearance", _theme);
        Row(general, "Download folder", FolderPicker());
        Wide(general, _categories, "Files go into Video, Music, Documents, Compressed, Programs or General inside the download folder.");
        Wide(general, _startWithWindows, "OSDM starts hidden in the taskbar tray, ready to catch downloads from your browser.");

        var connections = Page("Connections");
        Row(connections, "Connections per download", _connections,
            "Each file is split into parts that download at the same time. 8 suits most servers.");
        Row(connections, "Downloads at the same time", _simultaneous);
        var speed = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        _limitSpeed.Margin = new Padding(0, S(5), S(8), 0);
        var kbps = new Label { Text = "KB/s", AutoSize = true, Margin = new Padding(S(6), S(7), 0, 0) };
        speed.Controls.AddRange(new Control[] { _limitSpeed, _speedLimit, kbps });
        Row(connections, "Speed limit", speed, "For all downloads together, so browsing stays fast.");
        Row(connections, "Retries per connection", _retries);
        Row(connections, "Proxy", _proxy, "Example: http://127.0.0.1:8080 or socks5://127.0.0.1:1080");

        var browser = Page("Browser");
        Wide(browser, _confirm, "When off, downloads from the browser start right away.");
        Wide(browser, _clipboard, "When you copy a link to one of the file types below, OSDM offers to download it.");
        Row(browser, "File types to catch", _types, "File extensions, separated by spaces.");
        Row(browser, "Extension port", _port, "The extension talks to OSDM on this port. Change it in the extension too, then restart OSDM.");
        var setup = new Button { Text = "Browser extension setup...", AutoSize = true };
        setup.Click += (_, _) => showExtensionHelp(this);
        Wide(browser, setup);

        var video = Page("Video");
        Row(video, "Quality", _quality);
        Row(video, "File format", _container, "Video and audio are downloaded separately, then merged with FFmpeg.");
        var tools = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        _toolsStatus.Margin = new Padding(0, S(7), S(12), 0);
        tools.Controls.AddRange(new Control[] { _toolsStatus, _installTools });
        Row(video, "Video tools", tools, "yt-dlp finds the video on a page and FFmpeg merges it. Both are free and open source.");
        _installTools.Click += async (_, _) => await InstallToolsAsync();

        // Navigation
        _nav.Width = S(190);
        _nav.Padding = new Padding(0, S(14), 0, 0);
        _nav.Controls.Add(NavButton("General", "settings", t => t.TextMuted, general));
        _nav.Controls.Add(NavButton("Connections", "unfinished", t => t.Accent, connections));
        _nav.Controls.Add(NavButton("Browser", "globe", t => t.Teal, browser));
        _nav.Controls.Add(NavButton("Video", "video", t => t.Purple, video));
        _nav.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Current.Border);
            e.Graphics.DrawLine(pen, _nav.Width - 1, 0, _nav.Width - 1, _nav.Height);
        };

        // OK / Cancel
        var ok = new Button { Text = "Save", Tag = "primary", MinimumSize = new Size(S(100), S(34)), AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, MinimumSize = new Size(S(90), S(34)), AutoSize = true, Margin = new Padding(0, 0, S(8), 0) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false };
        buttons.Controls.AddRange(new Control[] { ok, cancel });
        var footer = new Panel { Dock = DockStyle.Bottom, Height = S(60), Padding = new Padding(S(20), S(13), S(20), S(13)) };
        footer.Controls.Add(buttons);
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Current.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        ok.Click += (_, _) => Save();

        Controls.Add(_pages);
        Controls.Add(_nav);
        Controls.Add(footer);
        AcceptButton = ok;
        CancelButton = cancel;

        ShowValues(current, startWithWindows);
        Theme.Current.Apply(this);
        _nav.BackColor = Theme.Current.Sidebar;
        ((RadioButton)_nav.Controls[0]).Checked = true;
        ActiveControl = _nav.Controls[0];
    }

    int S(int px) => LogicalToDeviceUnits(px);

    void ShowValues(Settings s, bool startWithWindows)
    {
        _theme.SelectedIndex = Math.Max(0, Array.IndexOf(Themes, s.Theme));
        _folder.Text = s.DownloadFolder;
        _categories.Checked = s.SortIntoCategories;
        _startWithWindows.Checked = startWithWindows;

        _connections.Value = Math.Clamp(s.ConnectionsPerDownload, 1, 32);
        _simultaneous.Value = Math.Clamp(s.MaxSimultaneous, 1, 20);
        _limitSpeed.Checked = s.SpeedLimitKBps > 0;
        _speedLimit.Value = s.SpeedLimitKBps > 0 ? Math.Clamp(s.SpeedLimitKBps, 1, 10_000_000) : 1024;
        _speedLimit.Enabled = _limitSpeed.Checked;
        _limitSpeed.CheckedChanged += (_, _) => _speedLimit.Enabled = _limitSpeed.Checked;
        _retries.Value = Math.Clamp(s.Retries, 0, 50);
        _proxy.Text = s.Proxy;

        _confirm.Checked = s.ConfirmBrowserDownloads;
        _clipboard.Checked = s.MonitorClipboard;
        _types.Text = s.CaptureExtensions;
        _port.Value = Math.Clamp(s.BridgePort, 1024, 65535);

        int quality = _qualityFormats.IndexOf(s.VideoFormat);
        if (quality < 0)
        {
            _quality.Items.Add("Custom: " + s.VideoFormat);
            _qualityFormats.Add(s.VideoFormat);
            quality = _qualityFormats.Count - 1;
        }
        _quality.SelectedIndex = quality;
        _container.SelectedIndex = Math.Max(0, _container.Items.IndexOf(s.VideoContainer));
        UpdateToolsStatus();
    }

    void Save()
    {
        string folder = _folder.Text.Trim();
        if (folder.Length == 0 || !Path.IsPathFullyQualified(folder))
        {
            MessageBox.Show(this, "Choose a full folder path for downloads, like C:\\Users\\you\\Downloads.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var s = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(_current))!;
        s.Theme = Themes[_theme.SelectedIndex];
        s.DownloadFolder = folder;
        s.SortIntoCategories = _categories.Checked;
        s.ConnectionsPerDownload = (int)_connections.Value;
        s.MaxSimultaneous = (int)_simultaneous.Value;
        s.SpeedLimitKBps = _limitSpeed.Checked ? (int)_speedLimit.Value : 0;
        s.Retries = (int)_retries.Value;
        s.Proxy = _proxy.Text.Trim();
        s.ConfirmBrowserDownloads = _confirm.Checked;
        s.MonitorClipboard = _clipboard.Checked;
        s.CaptureExtensions = string.Join(' ', _types.Text
            .Split(new[] { ' ', ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.TrimStart('.', '*').ToLowerInvariant())
            .Where(x => x.Length > 0)
            .Distinct());
        s.BridgePort = (int)_port.Value;
        s.VideoFormat = _qualityFormats[_quality.SelectedIndex];
        s.VideoContainer = (string)_container.SelectedItem!;
        Result = s.Normalize();
        DialogResult = DialogResult.OK;
    }

    async Task InstallToolsAsync()
    {
        _installTools.Enabled = false;
        _toolsStatus.Text = "Installing (progress in the status bar)...";
        await _installVideoTools();
        UpdateToolsStatus();
    }

    void UpdateToolsStatus()
    {
        _toolsStatus.Text = Tools.Ready ? "yt-dlp and FFmpeg are installed" : "Not installed (about 150 MB)";
        _installTools.Visible = !Tools.Ready;
        _installTools.Enabled = true;
    }

    Control FolderPicker()
    {
        var box = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        box.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _folder.Margin = new Padding(0, S(1), S(8), 0);
        var browse = new Button { Text = "Browse...", AutoSize = true, Margin = Padding.Empty };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _folder.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK) _folder.Text = dlg.SelectedPath;
        };
        box.Controls.Add(_folder, 0, 0);
        box.Controls.Add(browse, 1, 0);
        return box;
    }

    TableLayoutPanel Page(string title)
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Visible = false,
            Padding = new Padding(S(28), S(20), S(28), S(16)),
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var heading = new Label { Text = title, AutoSize = true, Font = _headingFont, Margin = new Padding(0, 0, 0, S(14)) };
        page.Controls.Add(heading, 0, 0);
        page.SetColumnSpan(heading, 2);
        _pages.Controls.Add(page);
        return page;
    }

    static int NextRow(TableLayoutPanel page) => page.RowCount++;

    void Row(TableLayoutPanel page, string label, Control control, string? hint = null)
    {
        int row = NextRow(page);
        page.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(8), S(20), S(8)) }, 0, row);
        control.Margin = new Padding(0, S(5), 0, S(5));
        if (control is not (TextBox or TableLayoutPanel)) control.Anchor = AnchorStyles.Left;
        page.Controls.Add(control, 1, row);
        if (hint != null) Hint(page, hint, column: 1);
    }

    void Wide(TableLayoutPanel page, Control control, string? hint = null)
    {
        int row = NextRow(page);
        control.Margin = new Padding(0, S(10), 0, S(2));
        page.Controls.Add(control, 0, row);
        page.SetColumnSpan(control, 2);
        if (hint != null) Hint(page, hint, column: 0);
    }

    void Hint(TableLayoutPanel page, string text, int column)
    {
        int row = NextRow(page);
        var label = new Label
        {
            Text = text, AutoSize = true, Tag = "muted",
            MaximumSize = new Size(S(column == 0 ? 480 : 340), 0),
            Margin = new Padding(column == 0 ? S(20) : 0, 0, 0, S(8)),
        };
        page.Controls.Add(label, column, row);
        if (column == 0) page.SetColumnSpan(label, 2);
    }

    RadioButton NavButton(string text, string icon, Func<Theme, Color> color, Control page)
    {
        var button = new RadioButton
        {
            Text = text,
            Appearance = Appearance.Button,
            Size = new Size(S(170), S(38)),
            Margin = new Padding(S(10), S(2), S(10), S(2)),
            Padding = new Padding(S(10), 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            ImageAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Image = Icons.Get(icon, S(18), color(Theme.Current)),
        };
        button.CheckedChanged += (_, _) =>
        {
            if (!button.Checked) return;
            foreach (Control p in _pages.Controls) p.Visible = p == page;
        };
        return button;
    }

    static ComboBox List(params string[] items)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        box.Items.AddRange(items);
        return box;
    }

    static CheckBox Check(string text) => new() { Text = text, AutoSize = true };

    static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max };

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Current.ApplyTitleBar(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _headingFont.Dispose();
        base.Dispose(disposing);
    }
}
