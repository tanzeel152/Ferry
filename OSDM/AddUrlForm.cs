using OSDM.Core;

namespace OSDM;

/// <summary>"Add download" window: link, file or video (and its quality), folder, optional file name, start now or later.</summary>
sealed class AddUrlForm : Form
{
    readonly TextBox _url = new() { Dock = DockStyle.Fill };
    readonly RadioButton _file = new() { Text = "File", Checked = true };
    readonly RadioButton _video = new() { Text = "Video page" };
    readonly ComboBox _quality = new() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left };
    readonly List<(string Label, string Format)> _qualities = new();
    readonly TextBox _folder = new() { Dock = DockStyle.Fill, PlaceholderText = "Automatic (sorted into a folder by type)" };
    readonly TextBox _name = new() { Dock = DockStyle.Fill, PlaceholderText = "Automatic" };
    readonly CheckBox _startNow = new() { Text = "Start now", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
    readonly Font _titleFont;
    readonly AddRequest _request;

    public AddRequest Request => _request;
    public string? Folder => string.IsNullOrWhiteSpace(_folder.Text) ? null : _folder.Text.Trim();
    public string? FileNameOverride => _video.Checked || string.IsNullOrWhiteSpace(_name.Text) ? null : _name.Text.Trim();
    public bool StartNow => _startNow.Checked;

    /// <param name="defaultFormat">The video quality from Settings, selected unless the browser sent one.</param>
    public AddUrlForm(AddRequest? request = null, string title = "Add download", string defaultFormat = "bv*+ba/b")
    {
        _request = request ?? new AddRequest();
        _titleFont = new Font(Font.FontFamily, Font.Size * 1.3f, FontStyle.Bold);
        Text = title;
        Icon = MainForm.LoadAppIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(S(640), S(384));
        ShowInTaskbar = request != null; // windows opened by the browser need to be findable

        // Header: icon, title and a one-line hint.
        var header = new Panel { Dock = DockStyle.Top, Height = S(76) };
        header.Paint += (_, e) =>
        {
            var t = Theme.Current;
            int circle = S(42), icon = S(22);
            var c = new Rectangle(S(22), (header.Height - circle) / 2, circle, circle);
            Shapes.FillCircle(e.Graphics, t.Selection, c);
            e.Graphics.DrawImage(Icons.Get(_video.Checked ? "video" : "download", icon, _video.Checked ? t.Purple : t.Accent),
                new Rectangle(c.X + (circle - icon) / 2, c.Y + (circle - icon) / 2, icon, icon));
            TextRenderer.DrawText(e.Graphics, title, _titleFont, new Point(c.Right + S(14), c.Y - S(1)), t.Text);
            TextRenderer.DrawText(e.Graphics, "Paste a link to a file, or to a page with a video on it.", Font, new Point(c.Right + S(15), c.Y + S(23)), t.TextMuted);
            using var pen = new Pen(t.Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        // File / Video choice, shown as two joined buttons.
        foreach (var (choice, icon) in new[] { (_file, "file"), (_video, "video") })
        {
            choice.Appearance = Appearance.Button;
            choice.Tag = "segment";
            choice.Size = new Size(S(140), S(32));
            choice.Margin = new Padding(0, 0, S(6), 0);
            choice.TextImageRelation = TextImageRelation.ImageBeforeText;
            choice.ImageAlign = ContentAlignment.MiddleCenter;
            choice.TextAlign = ContentAlignment.MiddleCenter;
            choice.Image = Icons.Get(icon, S(16), icon == "video" ? Theme.Current.Purple : Theme.Current.TextMuted);
        }
        var kind = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(2), 0, S(2)) };
        kind.Controls.AddRange(new Control[] { _file, _video });

        var browse = new Button { Text = "Browse...", AutoSize = true, Margin = new Padding(S(8), 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = Folder ?? "" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _folder.Text = dlg.SelectedPath;
        };

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(S(22), S(16), S(22), S(4)), ColumnCount = 3 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // Quality: the one picked in the browser's menu (if any), then the presets from Settings.
        if (!string.IsNullOrWhiteSpace(_request.Format)) _qualities.Add((_request.Quality ?? "As chosen in the browser", _request.Format));
        _qualities.AddRange(VideoInfo.Presets);
        _quality.Items.AddRange(_qualities.Select(q => (object)q.Label).ToArray());
        int preset = Array.FindIndex(VideoInfo.Presets, p => p.Format == defaultFormat);
        _quality.SelectedIndex = !string.IsNullOrWhiteSpace(_request.Format) ? 0 : Math.Max(0, preset);
        _quality.Width = S(320);

        AddRow(grid, 0, "Link", _url);
        AddRow(grid, 1, "Type", kind);
        AddRow(grid, 2, "Quality", _quality);
        AddRow(grid, 3, "Save to", _folder, browse);
        AddRow(grid, 4, "File name", _name);
        grid.RowCount = 6;
        for (int i = 0; i < 5; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Bottom bar: start now, Cancel, Download.
        var ok = new Button { Text = "Download", Tag = "primary", DialogResult = DialogResult.OK, MinimumSize = new Size(S(110), S(34)), AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, MinimumSize = new Size(S(90), S(34)), AutoSize = true, Margin = new Padding(0, 0, S(8), 0) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false };
        buttons.Controls.AddRange(new Control[] { ok, cancel });
        var footer = new Panel { Dock = DockStyle.Bottom, Height = S(60), Padding = new Padding(S(22), S(13), S(22), S(13)) };
        footer.Controls.Add(_startNow);
        footer.Controls.Add(buttons);
        _startNow.Location = new Point(S(22), (footer.Height - _startNow.PreferredSize.Height) / 2);
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Current.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        Controls.Add(grid);
        Controls.Add(header);
        Controls.Add(footer);
        AcceptButton = ok;
        CancelButton = cancel;

        _url.Text = _request.Url;
        _name.Text = _request.FileName ?? "";
        _video.Checked = string.Equals(_request.Kind, "video", StringComparison.OrdinalIgnoreCase) || VideoSites.IsVideoLink(_request.Url);
        _url.TextChanged += (_, _) => { if (VideoSites.IsVideoLink(_url.Text)) _video.Checked = true; };
        _video.CheckedChanged += (_, _) =>
        {
            _name.Enabled = !_video.Checked;
            _quality.Enabled = _video.Checked;
            header.Invalidate();
        };
        _name.Enabled = !_video.Checked;
        _quality.Enabled = _video.Checked;
        Shown += (_, _) =>
        {
            _url.Focus();
            _url.SelectAll();
        };

        ok.Click += (_, _) =>
        {
            if (!Uri.TryCreate(_url.Text.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                MessageBox.Show(this, "Enter a full http:// or https:// address.", "OSDM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            _request.Url = uri.ToString();
            _request.Kind = _video.Checked ? "video" : "file";
            (_request.Quality, _request.Format) = _video.Checked ? _qualities[_quality.SelectedIndex] : (null, null);
        };

        Theme.Current.Apply(this);
    }

    int S(int px) => LogicalToDeviceUnits(px);

    void AddRow(TableLayoutPanel grid, int row, string label, Control field, Control? extra = null)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(8), S(14), S(8)) }, 0, row);
        field.Margin = new Padding(0, S(5), 0, S(5));
        grid.Controls.Add(field, 1, row);
        if (extra != null) grid.Controls.Add(extra, 2, row);
        else grid.SetColumnSpan(field, 2);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Current.ApplyTitleBar(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _titleFont.Dispose();
        base.Dispose(disposing);
    }
}
