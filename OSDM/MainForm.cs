using Microsoft.Win32;
using OSDM.Core;

namespace OSDM;

/// <summary>Main window: category sidebar, toolbar, download list, details panel, status bar and tray icon.</summary>
sealed partial class MainForm : Form
{
    static readonly string[] Categories = { "Video", "Music", "Documents", "Compressed", "Programs", "General" };

    /// <summary>A menu or toolbar item's icon, redrawn in the theme's colors when the theme changes.</summary>
    sealed record IconSpec(string Name, Func<Theme, Color> Color);

    readonly DownloadManager _mgr = new();
    readonly BrowserBridge _bridge = new();
    readonly DownloadListView _list = new() { Dock = DockStyle.Fill };
    readonly EmptyState _empty = new() { Dock = DockStyle.Fill, Visible = false };
    readonly DetailsPanel _details = new() { Dock = DockStyle.Bottom };
    readonly ToolStripTextBox _search = new();
    readonly StatusStrip _status = new();
    readonly ToolStripStatusLabel _speedLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripStatusLabel _bridgeLabel = new();
    readonly ToolStripStatusLabel _toolsLabel = new();
    readonly NotifyIcon _tray = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    readonly Sidebar _sidebar;
    readonly MenuStrip _menu;
    readonly ToolStrip _toolbar;
    readonly string? _startupUrl;

    // Toolbar buttons that are enabled only when they would do something.
    ToolStripItem _resumeButton = null!, _pauseButton = null!, _pauseAllButton = null!, _deleteButton = null!, _clearButton = null!;

    bool _exiting, _trayHintShown, _startHidden, _started, _bridgeOk;
    string? _lastClipboard;
    int _tick;

    public MainForm(string? startupUrl, bool startHidden = false)
    {
        _startupUrl = startupUrl;
        _startHidden = startHidden;
        Theme.Use(_mgr.Settings.Theme);
        Text = "OSDM - Open Source Download Manager";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(S(1260), S(720));
        MinimumSize = new Size(S(860), S(500));
        StartPosition = FormStartPosition.CenterScreen;
        Icon = LoadAppIcon();
        AllowDrop = true;
        KeyPreview = true;

        _sidebar = new Sidebar(SidebarEntries(), Icon) { Dock = DockStyle.Left, Width = S(220) };
        _sidebar.SelectedChanged += () => RefreshList(force: true);

        _list.ContextMenuStrip = BuildListMenu();
        _list.DoubleClick += (_, _) => OpenOrToggle();
        _list.SelectedIndexChanged += (_, _) => OnSelectionChanged();
        _list.VirtualItemsSelectionRangeChanged += (_, _) => OnSelectionChanged();
        _details.Height = S(150);

        _toolbar = BuildToolbar();
        _toolbar.SizeChanged += (_, _) => _sidebar.HeaderHeight = _toolbar.Height;
        _menu = BuildMenu();
        MainMenuStrip = _menu;

        var listHost = new Panel { Dock = DockStyle.Fill };
        listHost.Controls.Add(_list);
        listHost.Controls.Add(_empty);
        var main = new Panel { Dock = DockStyle.Fill };
        main.Controls.Add(listHost);
        main.Controls.Add(_details);
        main.Controls.Add(_toolbar);

        _status.SizingGrip = false;
        _status.RenderMode = ToolStripRenderMode.ManagerRenderMode;
        _status.ShowItemToolTips = true;
        _status.Padding = new Padding(S(14), S(3), S(14), S(3));
        _toolsLabel.Margin = _bridgeLabel.Margin = new Padding(S(18), 0, 0, 0);
        _toolsLabel.ImageScaling = _bridgeLabel.ImageScaling = ToolStripItemImageScaling.None;
        _status.Items.AddRange(new ToolStripItem[] { _speedLabel, _toolsLabel, _bridgeLabel });

        // Docked last-added first: status bar, menu bar, sidebar, then the main area fills the rest.
        Controls.Add(main);
        Controls.Add(_sidebar);
        Controls.Add(_menu);
        Controls.Add(_status);

        _tray.Icon = new Icon(Icon, SystemInformation.SmallIconSize);
        _tray.Text = "OSDM";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Items.Add(Item("Open OSDM", null, null, ShowFromTray));
        _tray.ContextMenuStrip.Items.Add(Item("Add download...", "add", t => t.Accent, () => { ShowFromTray(); AddDownload(null); }));
        _tray.ContextMenuStrip.Items.Add(Item("Pause all", "pauseall", t => t.Amber, () => _mgr.PauseAll()));
        _tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add(Item("Exit", "exit", t => t.TextMuted, ExitApp));

        _mgr.ItemCompleted += item => BeginInvokeSafe(() =>
            _tray.ShowBalloonTip(4000, "Download complete", Format.DisplayName(item), ToolTipIcon.Info));
        _bridge.OnAdd = request =>
        {
            BeginInvokeSafe(() => HandleIncoming(request, "Download from browser"));
            return true;
        };
        _bridge.OnShow = () => BeginInvokeSafe(ShowFromTray);
        _bridge.OnFormats = request => VideoInfo.GetAsync(request, _mgr.Settings);

        _timer.Tick += (_, _) => OnTimer();
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.UnicodeText) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.UnicodeText) is string text) AddDownload(new AddRequest { Url = text.Trim() }); };
        KeyDown += OnKeyDown;
        FormClosing += OnFormClosing;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        ApplyTheme();
    }

    static IEnumerable<Sidebar.Entry> SidebarEntries() => new Sidebar.Entry[]
    {
        new("all", "All downloads", "inbox", t => t.Accent),
        new("unfinished", "Unfinished", "unfinished", t => t.Accent),
        new("finished", "Finished", "finished", t => t.Green),
        new(null, "Categories"),
        new("Video", "Video", "video", t => t.Purple),
        new("Music", "Music", "music", t => t.Pink),
        new("Documents", "Documents", "document", t => t.Accent),
        new("Compressed", "Compressed", "archive", t => t.Amber),
        new("Programs", "Programs", "program", t => t.Teal),
        new("General", "General", "file", t => t.TextMuted),
    };

    int S(int px) => LogicalToDeviceUnits(px);

    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden)
        {
            // Started with Windows: create the window (so it can receive work) but keep it hidden; the tray icon is the UI.
            _startHidden = false;
            value = false;
            if (!IsHandleCreated) CreateHandle();
            StartUp();
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Current.ApplyTitleBar(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        StartUp();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyTheme();
    }

    void StartUp()
    {
        if (_started) return;
        _started = true;
        _bridgeOk = _bridge.Start(_mgr.Settings.BridgePort);
        _bridgeLabel.Text = _bridgeOk ? $"Browser extension: port {_bridge.Port}" : $"Browser extension: port {_bridge.Port} unavailable";
        _bridgeLabel.ToolTipText = _bridgeOk ? "OSDM is ready for downloads from the browser extension" : _bridge.StartError;
        UpdateStatusLabels();
        if (Tools.HasOldYtDlp) _ = InstallToolsAsync(quiet: true); // replaces yt-dlp.exe with the version Smart App Control allows
        try { _lastClipboard = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch { }
        RefreshList(force: true);
        _timer.Start();
        if (_startupUrl != null) AddDownload(new AddRequest { Url = _startupUrl });
    }

    /// <summary>Colors everything for the current theme setting, and redraws the icons.</summary>
    void ApplyTheme()
    {
        Theme.Use(_mgr.Settings.Theme);
        var t = Theme.Current;
        BackColor = t.Background;
        ForeColor = t.Text;
        t.ApplyTitleBar(this);
        _list.ApplyTheme();
        _search.BackColor = t.Content;
        _search.ForeColor = t.Text;
        ApplyIcons(_toolbar.Items, S(24));
        foreach (var strip in new ToolStrip?[] { _menu, _list.ContextMenuStrip, _tray.ContextMenuStrip })
            if (strip != null) ApplyIcons(strip.Items, S(16));
        UpdateStatusLabels();
        Invalidate(true);
    }

    static void ApplyIcons(ToolStripItemCollection items, int size)
    {
        foreach (ToolStripItem item in items)
        {
            if (item.Tag is IconSpec spec)
            {
                item.Image = Icons.Get(spec.Name, size, spec.Color(Theme.Current));
                item.ImageScaling = ToolStripItemImageScaling.None;
            }
            if (item is ToolStripDropDownItem { HasDropDownItems: true } parent) ApplyIcons(parent.DropDownItems, size);
        }
    }

    void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Follow Windows when it switches between light and dark mode.
        if (e.Category == UserPreferenceCategory.General && _mgr.Settings.Theme == "System") BeginInvokeSafe(ApplyTheme);
    }

    void UpdateStatusLabels()
    {
        var t = Theme.Current;
        int dot = S(8);
        if (!_installingTools) _toolsLabel.Text = Tools.Ready ? "Video tools ready" : "Video tools not installed";
        _toolsLabel.ToolTipText = Tools.Ready ? "yt-dlp and FFmpeg are installed" : "Tools > Install video tools";
        _toolsLabel.Image = Shapes.Dot(dot, Tools.Ready ? t.Green : t.TextMuted);
        _bridgeLabel.Image = Shapes.Dot(dot, _bridgeOk ? t.Green : t.Red);
    }

    static bool InCategory(DownloadItem item, string key) => key switch
    {
        "unfinished" => item.Status != DownloadStatus.Completed,
        "finished" => item.Status == DownloadStatus.Completed,
        "Video" => item.Kind == DownloadKind.Video || FileNames.CategoryFor(item.FileName) == "Video",
        _ when Categories.Contains(key) => item.Kind == DownloadKind.File && FileNames.CategoryFor(item.FileName) == key,
        _ => true,
    };

    bool InView(DownloadItem item)
    {
        if (!InCategory(item, _sidebar.SelectedKey)) return false;
        string query = _search.Text.Trim();
        return query.Length == 0
            || Format.DisplayName(item).Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Url.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    void RefreshList(bool force = false)
    {
        var all = _mgr.Items;
        _list.SetRows(all.Where(InView).ToList(), force);

        bool empty = _list.Rows.Count == 0;
        if (empty)
        {
            var (title, hint) = EmptyText();
            _empty.SetText(title, hint);
        }
        if (_empty.Visible != empty)
        {
            _empty.Visible = empty;
            _list.Visible = !empty;
        }

        _sidebar.SetCounts(new[] { "all", "unfinished", "finished" }.Concat(Categories)
            .ToDictionary(k => k, k => all.Count(i => InCategory(i, k))));
        _details.Item = _list.SelectedRows().FirstOrDefault();

        double speed = all.Where(i => i.Status is DownloadStatus.Downloading or DownloadStatus.Merging).Sum(i => i.Speed);
        int active = all.Count(i => _mgr.IsRunning(i));
        _speedLabel.Text = active > 0 ? $"{active} downloading  ·  {Format.Speed(speed)}" : all.Count == 1 ? "1 download" : $"{all.Count} downloads";
        _tray.Text = active > 0 ? $"OSDM: {active} downloading, {Format.Speed(speed)}" : "OSDM";
        UpdateCommands(all);
    }

    (string Title, string Hint) EmptyText()
    {
        string query = _search.Text.Trim();
        if (query.Length > 0) return ("No matches", $"No downloads match \"{query}\".");
        return _sidebar.SelectedKey switch
        {
            "all" => ("No downloads yet", "Click Add URL, press Ctrl+V with a link copied, or download a file in your browser."),
            "unfinished" => ("Nothing in progress", "Downloads that are running, paused or failed show up here."),
            "finished" => ("No finished downloads", "Completed downloads show up here."),
            _ => ($"No {_sidebar.SelectedText.ToLowerInvariant()} downloads", $"{_sidebar.SelectedText} files you download show up here."),
        };
    }

    void OnSelectionChanged()
    {
        _details.Item = _list.SelectedRows().FirstOrDefault();
        UpdateCommands(_mgr.Items);
    }

    void UpdateCommands(IReadOnlyList<DownloadItem> all)
    {
        var selected = _list.SelectedRows();
        _resumeButton.Enabled = selected.Any(i => i.Status is DownloadStatus.Paused or DownloadStatus.Error ||
                                                  (i.Status == DownloadStatus.Completed && !File.Exists(i.FullPath)));
        _pauseButton.Enabled = selected.Any(i => i.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Merging);
        _deleteButton.Enabled = selected.Count > 0;
        _pauseAllButton.Enabled = all.Any(i => i.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Merging);
        _clearButton.Enabled = all.Any(i => i.Status == DownloadStatus.Completed);
    }

    List<DownloadItem> SelectedItems() => _list.SelectedRows();

    internal void BeginInvokeSafe(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }

    internal static Icon LoadAppIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("osdm.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }
}
