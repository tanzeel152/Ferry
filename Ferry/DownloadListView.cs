using System.Runtime.InteropServices;
using Ferry.Core;

namespace Ferry;

/// <summary>The download list: a virtual, owner-drawn ListView with file icons, progress bars and colored status.</summary>
sealed class DownloadListView : ListView
{
    const int NameColumn = 0, ProgressColumn = 2, StatusColumn = 5, AddedColumn = 6;

    readonly Dictionary<string, Bitmap?> _fileIcons = new(StringComparer.OrdinalIgnoreCase);
    List<DownloadItem> _rows = new();
    int _hot = -1;
    bool _fitting;

    public DownloadListView()
    {
        DoubleBuffered = true;
        View = View.Details;
        FullRowSelect = true;
        VirtualMode = true;
        OwnerDraw = true;
        HideSelection = false;
        BorderStyle = BorderStyle.None;
        Columns.Add("Name", S(320));
        Columns.Add("Size", S(80), HorizontalAlignment.Right);
        Columns.Add("Progress", S(150));
        Columns.Add("Speed", S(85), HorizontalAlignment.Right);
        Columns.Add("Time left", S(80), HorizontalAlignment.Right);
        Columns.Add("Status", S(215));
        Columns.Add("Added", S(115));
        // The image list only sets the row height.
        SmallImageList = new ImageList { ImageSize = new Size(1, S(32)) };
    }

    public IReadOnlyList<DownloadItem> Rows => _rows;

    int S(int px) => LogicalToDeviceUnits(px);

    /// <summary>Shows these downloads, keeping the selection.</summary>
    public void SetRows(List<DownloadItem> rows, bool force)
    {
        bool same = !force && rows.Count == _rows.Count && rows.Zip(_rows).All(p => p.First == p.Second);
        if (!same)
        {
            var selectedIds = SelectedRows().Select(i => i.Id).ToHashSet();
            _rows = rows;
            VirtualListSize = rows.Count;
            SelectedIndices.Clear();
            for (int i = 0; i < rows.Count; i++)
                if (selectedIds.Contains(rows[i].Id)) SelectedIndices.Add(i);
            if (_hot >= rows.Count) _hot = -1;
            FitNameColumn();
        }
        Invalidate();
    }

    public List<DownloadItem> SelectedRows() =>
        SelectedIndices.Cast<int>().Where(i => i < _rows.Count).Select(i => _rows[i]).ToList();

    public void SelectAll()
    {
        for (int i = 0; i < _rows.Count; i++) SelectedIndices.Add(i);
    }

    public void ApplyTheme()
    {
        var t = Theme.Current;
        BackColor = t.Content;
        ForeColor = t.Text;
        if (!IsHandleCreated) return;
        // Dark scroll bars and header background (the parts of the control that are not owner-drawn).
        Theme.SetWindowTheme(Handle, t.IsDark ? "DarkMode_CFD" : null, null);
        IntPtr header = SendMessage(Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        if (header != IntPtr.Zero) Theme.SetWindowTheme(header, t.IsDark ? "DarkMode_ItemsView" : "ItemsView", null);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTheme();
    }

    protected override void OnRetrieveVirtualItem(RetrieveVirtualItemEventArgs e)
    {
        // Text is used for keyboard search and accessibility; drawing is done below.
        var item = e.ItemIndex < _rows.Count ? _rows[e.ItemIndex] : null;
        e.Item = item == null ? new ListViewItem(new string[Columns.Count]) : new ListViewItem(new[]
        {
            Format.DisplayName(item), Format.Size(item.TotalSize), $"{item.Progress * 100:F0}%",
            Format.Speed(item.Speed), Format.TimeLeft(item), Format.Status(item), Format.Date(item.Added),
        });
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        var t = Theme.Current;
        using (var back = new SolidBrush(t.Content)) e.Graphics.FillRectangle(back, e.Bounds);
        using (var pen = new Pen(t.Border)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        var text = Rectangle.Inflate(e.Bounds, -S(12), 0);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text, Font, text, t.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | Align(e.Header?.TextAlign ?? HorizontalAlignment.Left));
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        // Cells are drawn in OnDrawSubItem.
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _rows.Count) return;
        var t = Theme.Current;
        var g = e.Graphics;
        var item = _rows[e.ItemIndex];
        // For the first column, Bounds is the whole row.
        var bounds = e.ColumnIndex == NameColumn ? e.Bounds with { Width = Columns[NameColumn].Width } : e.Bounds;

        bool selected = SelectedIndices.Contains(e.ItemIndex);
        using (var back = new SolidBrush(selected ? t.Selection : e.ItemIndex == _hot ? t.RowHover : t.Content))
            g.FillRectangle(back, bounds);

        var cell = Rectangle.Inflate(bounds, -S(12), 0);
        switch (e.ColumnIndex)
        {
            case NameColumn: DrawName(g, item, cell, t); break;
            case ProgressColumn: DrawProgress(g, item, cell, t); break;
            case StatusColumn: DrawStatus(g, item, cell, t); break;
            default:
                string text = e.SubItem?.Text ?? "";
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | Align(Columns[e.ColumnIndex].TextAlign);
                TextRenderer.DrawText(g, text, Font, cell, e.ColumnIndex == AddedColumn ? t.TextMuted : t.Text, flags);
                break;
        }
    }

    void DrawName(Graphics g, DownloadItem item, Rectangle cell, Theme t)
    {
        int size = S(16);
        var iconRect = new Rectangle(cell.X, cell.Y + (cell.Height - size) / 2, size, size);
        var icon = item.Kind == DownloadKind.Video && item.FileName.Length == 0
            ? Icons.Get("video", size, t.Purple)
            : FileIcon(item.FileName) ?? Icons.Get("file", size, t.TextMuted);
        g.DrawImage(icon, iconRect);
        var text = new Rectangle(iconRect.Right + S(10), cell.Y, cell.Right - iconRect.Right - S(10), cell.Height);
        TextRenderer.DrawText(g, Format.DisplayName(item), Font, text, t.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void DrawProgress(Graphics g, DownloadItem item, Rectangle cell, Theme t)
    {
        bool done = item.Status == DownloadStatus.Completed;
        double progress = done ? 1 : item.Progress;
        bool known = done || item.TotalSize > 0;
        string text = !known ? Format.Size(item.Downloaded) : progress >= 1 ? "100%" : $"{progress * 100:F1}%";

        int textWidth = S(52);
        int height = S(6);
        var bar = new Rectangle(cell.X, cell.Y + (cell.Height - height) / 2, Math.Max(0, cell.Width - textWidth - S(8)), height);
        Shapes.FillRounded(g, t.Track, bar, height / 2f);
        if (known && progress > 0)
            Shapes.FillRounded(g, t.StatusColor(item.Status), bar with { Width = Math.Max(height, (int)(bar.Width * progress)) }, height / 2f);
        TextRenderer.DrawText(g, text, Font, new Rectangle(bar.Right + S(8), cell.Y, textWidth, cell.Height), t.TextMuted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    void DrawStatus(Graphics g, DownloadItem item, Rectangle cell, Theme t)
    {
        int d = S(8);
        Shapes.FillCircle(g, t.StatusColor(item.Status), new RectangleF(cell.X, cell.Y + (cell.Height - d) / 2f, d, d));
        var text = new Rectangle(cell.X + d + S(8), cell.Y, cell.Width - d - S(8), cell.Height);
        TextRenderer.DrawText(g, Format.Status(item), Font, text, item.Status == DownloadStatus.Error ? t.Red : t.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    static TextFormatFlags Align(HorizontalAlignment a) => a switch
    {
        HorizontalAlignment.Right => TextFormatFlags.Right,
        HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
        _ => TextFormatFlags.Left,
    };

    // Row hover highlight.
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHot(GetItemAt(e.X, e.Y)?.Index ?? -1);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHot(-1);
    }

    void SetHot(int index)
    {
        if (index == _hot) return;
        InvalidateRow(_hot);
        _hot = index;
        InvalidateRow(_hot);
    }

    void InvalidateRow(int index)
    {
        if (index >= 0 && index < VirtualListSize) Invalidate(GetItemRect(index));
    }

    // The name column takes whatever width the other columns leave.
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FitNameColumn();
    }

    protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e)
    {
        base.OnColumnWidthChanged(e);
        if (e.ColumnIndex != NameColumn) FitNameColumn();
    }

    void FitNameColumn()
    {
        if (_fitting || Columns.Count == 0) return;
        _fitting = true;
        int others = 0;
        for (int i = 1; i < Columns.Count; i++) others += Columns[i].Width;
        Columns[NameColumn].Width = Math.Max(S(180), ClientSize.Width - others);
        _fitting = false;
    }

    /// <summary>The Windows icon for this file type (cached by extension).</summary>
    Bitmap? FileIcon(string fileName)
    {
        string ext = Path.GetExtension(fileName);
        if (!_fileIcons.TryGetValue(ext, out var bmp)) _fileIcons[ext] = bmp = ShellIcon("file" + ext);
        return bmp;
    }

    static Bitmap? ShellIcon(string fileName)
    {
        var info = new SHFILEINFO();
        IntPtr result = SHGetFileInfo(fileName, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            using var icon = Icon.FromHandle(info.hIcon);
            return icon.ToBitmap();
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var bmp in _fileIcons.Values) bmp?.Dispose();
            SmallImageList?.Dispose();
        }
        base.Dispose(disposing);
    }

    const int LVM_GETHEADER = 0x101F;
    const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
