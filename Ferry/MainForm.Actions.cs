using System.Diagnostics;
using Ferry.Core;

namespace Ferry;

sealed partial class MainForm
{
    bool _installingTools;

    static ToolStripMenuItem Item(string text, string? icon, Func<Theme, Color>? color, Action onClick, Keys keys = Keys.None)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => onClick(), keys);
        if (icon != null) item.Tag = new IconSpec(icon, color ?? (t => t.TextMuted));
        return item;
    }

    MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Padding = new Padding(S(6), S(2), 0, S(2)) };

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(Item("&Add download...", "add", t => t.Accent, () => AddDownload(null), Keys.Control | Keys.N));
        file.DropDownItems.Add(Item("Add &video...", "video", t => t.Purple, () => AddDownload(new AddRequest { Kind = "video" })));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("E&xit", "exit", null, ExitApp));

        var downloads = new ToolStripMenuItem("&Downloads");
        downloads.DropDownItems.Add(Item("&Resume", "resume", t => t.Green, () => ForSelected(_mgr.Start)));
        downloads.DropDownItems.Add(Item("&Pause", "pause", t => t.Amber, () => ForSelected(_mgr.Pause)));
        downloads.DropDownItems.Add(Item("Re&start from beginning", "restart", null, RestartSelected));
        downloads.DropDownItems.Add(new ToolStripSeparator());
        downloads.DropDownItems.Add(Item("Resume &all", null, null, () => _mgr.StartAll()));
        downloads.DropDownItems.Add(Item("Pause a&ll", "pauseall", t => t.Amber, () => _mgr.PauseAll()));
        downloads.DropDownItems.Add(new ToolStripSeparator());
        downloads.DropDownItems.Add(Item("&Delete", "delete", t => t.Red, () => DeleteSelected(false)));
        downloads.DropDownItems.Add(Item("Clear &finished from list", "clear", t => t.Green, ClearFinished));

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add(Item("&Settings...", "settings", null, ShowSettings));
        tools.DropDownItems.Add(Item("Open download &folder", "folder", t => t.Amber, () => OpenPath(_mgr.Settings.DownloadFolder, folder: true)));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(Item("Install video tools (yt-dlp + FFmpeg)", "tools", t => t.Purple, () => _ = InstallToolsAsync()));
        tools.DropDownItems.Add(Item("&Browser extension setup...", "globe", t => t.Teal, () => ShowExtensionHelp()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        var startWithWindows = Item("Start Ferry when &Windows starts", null, null, ToggleStartWithWindows);
        tools.DropDownItems.Add(startWithWindows);
        tools.DropDownOpening += (_, _) => startWithWindows.Checked = StartWithWindows.Enabled;

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(Item("&Report a problem...", null, null, () => OpenWebPage(RepoUrl + "/issues/new/choose")));
        help.DropDownItems.Add(Item("Check for &updates...", "download", t => t.Accent, () => OpenWebPage(RepoUrl + "/releases/latest")));
        help.DropDownItems.Add(Item("&Source code on GitHub", "globe", t => t.Teal, () => OpenWebPage(RepoUrl)));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(Item("&About Ferry", "info", t => t.Accent, () => MessageBox.Show(this,
            $"Ferry {typeof(MainForm).Assembly.GetName().Version?.ToString(3)} - free, open-source download manager\n\n" +
            "Multi-connection downloads with pause/resume, browser integration and video downloads.\n\n" +
            "Video downloads use yt-dlp (Unlicense) and FFmpeg (GPL/LGPL), downloaded separately on request.\n\n" +
            $"Licensed under the GNU GPL v3. Source code, releases and bug reports:\n{RepoUrl}",
            "About Ferry", MessageBoxButtons.OK, MessageBoxIcon.Information)));

        menu.Items.AddRange(new ToolStripItem[] { file, downloads, tools, help });
        return menu;
    }

    ToolStrip BuildToolbar()
    {
        var bar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(S(10), S(6), S(10), S(6)),
            ShowItemToolTips = true,
        };
        ToolStripButton Button(string text, string icon, Func<Theme, Color> color, string tip, Action onClick)
        {
            var b = new ToolStripButton(text)
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageAboveText,
                ImageScaling = ToolStripItemImageScaling.None,
                Padding = new Padding(S(5), S(4), S(5), S(2)),
                Margin = new Padding(S(1), 0, S(1), 0),
                AutoToolTip = false,
                ToolTipText = tip,
                Tag = new IconSpec(icon, color),
            };
            b.Click += (_, _) => onClick();
            return b;
        }

        _search.Alignment = ToolStripItemAlignment.Right;
        _search.AutoSize = false;
        _search.Width = S(200);
        _search.Overflow = ToolStripItemOverflow.Never;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.Margin = new Padding(0, 0, S(6), 0);
        _search.TextBox.PlaceholderText = "Search downloads  (Ctrl+F)";
        _search.TextChanged += (_, _) => RefreshList(force: true);

        bar.Items.AddRange(new ToolStripItem[]
        {
            Button("Add URL", "add", t => t.Accent, "Download a file from a link (Ctrl+N)", () => AddDownload(null)),
            Button("Add video", "video", t => t.Purple, "Download a video from YouTube and other sites", () => AddDownload(new AddRequest { Kind = "video" })),
            new ToolStripSeparator(),
            _resumeButton = Button("Resume", "resume", t => t.Green, "Resume the selected downloads", () => ForSelected(_mgr.Start)),
            _pauseButton = Button("Pause", "pause", t => t.Amber, "Pause the selected downloads", () => ForSelected(_mgr.Pause)),
            _pauseAllButton = Button("Pause all", "pauseall", t => t.Amber, "Pause every download", () => _mgr.PauseAll()),
            new ToolStripSeparator(),
            _deleteButton = Button("Delete", "delete", t => t.Red, "Remove the selected downloads from the list (Del)", () => DeleteSelected(false)),
            _clearButton = Button("Clear finished", "clear", t => t.Green, "Remove finished downloads from the list (the files stay)", ClearFinished),
            new ToolStripSeparator(),
            Button("Open folder", "folder", t => t.Amber, "Open the folder of the selected download", OpenSelectedFolder),
            Button("Settings", "settings", t => t.TextMuted, "Settings", ShowSettings),
            Button("Extension", "globe", t => t.Teal, "Set up the browser extension", () => ShowExtensionHelp()),
            _search,
        });
        return bar;
    }

    ContextMenuStrip BuildListMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(Item("Open", "open", t => t.Accent, () => { if (SelectedItems().FirstOrDefault() is { } i) OpenPath(i.FullPath, folder: false); }));
        menu.Items.Add(Item("Open folder", "folder", t => t.Amber, OpenSelectedFolder));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Resume", "resume", t => t.Green, () => ForSelected(_mgr.Start)));
        menu.Items.Add(Item("Pause", "pause", t => t.Amber, () => ForSelected(_mgr.Pause)));
        menu.Items.Add(Item("Restart from beginning", "restart", null, RestartSelected));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Copy address", "copy", null, () => { if (SelectedItems().FirstOrDefault() is { } i) Clipboard.SetText(i.Url); }));
        menu.Items.Add(Item("Delete", "delete", t => t.Red, () => DeleteSelected(false)));
        menu.Items.Add(Item("Delete with file", null, null, () => DeleteSelected(true)));
        return menu;
    }

    void ForSelected(Action<DownloadItem> action)
    {
        foreach (var item in SelectedItems()) action(item);
        RefreshList();
    }

    async void RestartSelected()
    {
        foreach (var item in SelectedItems()) await _mgr.RestartAsync(item);
    }

    async void ClearFinished()
    {
        foreach (var item in _mgr.Items.Where(i => i.Status == DownloadStatus.Completed)) await _mgr.RemoveAsync(item, false);
        RefreshList(force: true);
    }

    /// <summary>Opens the download's folder with its file selected. While a video downloads, its final name is not
    /// known yet, so then only the folder opens (Ferry used to create an empty folder named after the file).</summary>
    void OpenSelectedFolder()
    {
        var item = SelectedItems().FirstOrDefault();
        if (item != null && item.FileName.Length > 0 && File.Exists(item.FullPath)) OpenPath(item.FullPath, folder: true);
        else OpenPath(item?.Folder is { Length: > 0 } folder ? folder : _mgr.Settings.DownloadFolder, folder: true);
    }

    void AddDownload(AddRequest? request) => HandleIncoming(request ?? new AddRequest(), "Add download", alwaysAsk: true);

    /// <summary>Shows the add window (or adds directly, if the user turned confirmation off for the browser).</summary>
    void HandleIncoming(AddRequest request, string title, bool alwaysAsk = false)
    {
        request.Kind ??= VideoSites.IsVideoLink(request.Url) ? "video" : "file";
        if (!alwaysAsk && !_mgr.Settings.ConfirmBrowserDownloads)
        {
            if (!EnsureVideoTools(request)) return;
            _mgr.Add(request);
            RefreshList();
            return;
        }

        using var form = new AddUrlForm(request, title, _mgr.Settings.VideoFormat);
        if (!Visible) form.TopMost = true;
        if (form.ShowDialog(Visible ? this : null) != DialogResult.OK) return;
        if (!EnsureVideoTools(form.Request)) return;
        _mgr.Add(form.Request, form.Folder, form.FileNameOverride, form.StartNow);
        RefreshList();
    }

    bool EnsureVideoTools(AddRequest request)
    {
        if (!string.Equals(request.Kind, "video", StringComparison.OrdinalIgnoreCase) || Tools.Ready || _installingTools) return true;
        var answer = MessageBox.Show(this,
            "Video downloads need yt-dlp and FFmpeg (free, open source, about 150 MB).\n\nDownload them now? The video will be added to the list either way.",
            "Ferry", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes) _ = InstallToolsAsync();
        return true;
    }

    /// <param name="quiet">Started by Ferry itself (an upgrade): a failure shows in the status bar, not in a message box.</param>
    async Task InstallToolsAsync(bool quiet = false)
    {
        if (Tools.Ready)
        {
            MessageBox.Show(this, $"yt-dlp and FFmpeg are already installed:\n{Tools.YtDlp}\n{Tools.FFmpeg}", "Ferry");
            return;
        }
        if (_installingTools) return;
        _installingTools = true;
        var progress = new Progress<string>(s => _toolsLabel.Text = s);
        string? failed = null;
        try
        {
            await Tools.InstallAsync(progress, CancellationToken.None);
            // Retry video downloads that failed because the tools were missing.
            foreach (var item in _mgr.Items.Where(i => i.Kind == DownloadKind.Video && i.Status == DownloadStatus.Error)) _mgr.Start(item);
        }
        catch (Exception ex)
        {
            failed = "Could not install the video tools:\n" + ex.Message;
            if (!quiet) MessageBox.Show(this, failed, "Ferry", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _installingTools = false;
            UpdateStatusLabels();
            if (failed != null) _toolsLabel.ToolTipText = failed + "\n\nTools > Install video tools";
        }
    }

    async void DeleteSelected(bool withFile)
    {
        var items = SelectedItems();
        if (items.Count == 0) return;
        string what = items.Count == 1 ? $"\"{Format.DisplayName(items[0])}\"" : $"{items.Count} downloads";
        string extra = withFile ? "\n\nThe downloaded files will be deleted too." : "";
        if (MessageBox.Show(this, $"Remove {what} from the list?{extra}", "Ferry", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        foreach (var item in items) await _mgr.RemoveAsync(item, withFile);
        RefreshList(force: true);
    }

    void OpenOrToggle()
    {
        if (SelectedItems().FirstOrDefault() is not { } item) return;
        if (item.Status == DownloadStatus.Completed) OpenPath(item.FullPath, folder: false);
        else if (_mgr.IsRunning(item)) _mgr.Pause(item);
        else _mgr.Start(item);
    }

    void OpenPath(string path, bool folder)
    {
        try
        {
            if (folder && File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (folder) { Directory.CreateDirectory(path); Process.Start("explorer.exe", $"\"{path}\""); }
            else if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ferry", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void OpenWebPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open {url}:\n{ex.Message}", "Ferry", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void ShowSettings()
    {
        using var form = new SettingsForm(_mgr.Settings, StartWithWindows.Enabled, () => InstallToolsAsync(), ShowExtensionHelp);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        int oldPort = _mgr.Settings.BridgePort;
        _mgr.ApplySettings(form.Result);
        if (form.StartWithWindowsChecked != StartWithWindows.Enabled)
            SetStartWithWindows(form.StartWithWindowsChecked);
        ApplyTheme();
        RefreshList(force: true);
        if (_mgr.Settings.BridgePort != oldPort)
            MessageBox.Show(this, "Restart Ferry to use the new extension port, and change it in the extension too.", "Ferry");
    }

    void ShowExtensionHelp(IWin32Window? owner = null)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "browser-extension");
        var answer = MessageBox.Show(owner ?? this,
            "To catch downloads and videos from Chrome or Edge:\n\n" +
            "1. Open chrome://extensions (or edge://extensions)\n" +
            "2. Turn on \"Developer mode\"\n" +
            "3. Click \"Load unpacked\" and choose this folder:\n   " + folder + "\n\n" +
            $"Ferry must be running. The extension talks to it on localhost:{_mgr.Settings.BridgePort}.\n\nOpen the folder now?",
            "Browser extension", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer == DialogResult.Yes) OpenPath(folder, folder: true);
    }

    void ToggleStartWithWindows() => SetStartWithWindows(!StartWithWindows.Enabled);

    void SetStartWithWindows(bool enabled)
    {
        try { StartWithWindows.Enabled = enabled; }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not change the Windows startup setting:\n" + ex.Message, "Ferry", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void OnTimer()
    {
        RefreshList();
        if (++_tick % 2 == 0 && _mgr.Settings.MonitorClipboard) CheckClipboard();
    }

    void CheckClipboard()
    {
        string? text;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : null; }
        catch (System.Runtime.InteropServices.ExternalException) { return; }
        if (text == null || text == _lastClipboard) return;
        _lastClipboard = text;
        if (text.Contains('\n') || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return;
        if (_mgr.Settings.IsCaptureType(uri.AbsolutePath) && !_mgr.Items.Any(i => i.Url == text))
            HandleIncoming(new AddRequest { Url = text }, "Download copied link", alwaysAsk: true);
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.F)
        {
            _search.Focus();
            _search.SelectAll();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape && _search.Focused && _search.Text.Length > 0)
        {
            _search.Clear();
            e.SuppressKeyPress = true;
        }
        else if (_search.Focused)
        {
            // Typing in the search box: leave Delete, Ctrl+V and Ctrl+A to it.
        }
        else if (e.KeyCode == Keys.Delete && _list.Focused) DeleteSelected(e.Shift);
        else if (e.Control && e.KeyCode == Keys.V)
        {
            try { if (Clipboard.ContainsText()) AddDownload(new AddRequest { Url = Clipboard.GetText().Trim() }); } catch { }
        }
        else if (e.Control && e.KeyCode == Keys.A && _list.Focused) _list.SelectAll();
    }

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exiting || e.CloseReason != CloseReason.UserClosing) { Shutdown(); return; }
        // Closing the window keeps Ferry running in the tray, so the browser can still send downloads.
        e.Cancel = true;
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.ShowBalloonTip(3000, "Ferry is still running", "Downloads continue in the background. Right-click the tray icon to exit.", ToolTipIcon.Info);
        }
    }

    internal void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    internal void ExitApp()
    {
        _exiting = true;
        Close();
    }

    void Shutdown()
    {
        _timer.Stop();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _tray.Visible = false;
        _bridge.Dispose();
        _mgr.Dispose();
        _tray.Dispose();
    }
}
