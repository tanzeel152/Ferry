namespace Ferry.Core;

/// <summary>Owns the download list: queueing, start/pause/remove, speed sampling and saving.</summary>
public sealed class DownloadManager : IDisposable
{
    readonly object _lock = new();
    readonly object _saveLock = new();
    readonly List<DownloadItem> _items;
    readonly Dictionary<Guid, (CancellationTokenSource Cts, Task Task)> _running = new();
    readonly Dictionary<Guid, long> _lastBytes = new();
    readonly Timer _timer;
    int _ticks;

    public Settings Settings { get; private set; }
    public SpeedLimiter Limiter { get; } = new();

    /// <summary>Raised (on a worker thread) when a download starts, stops, finishes or fails.</summary>
    public event Action<DownloadItem>? ItemChanged;
    public event Action<DownloadItem>? ItemCompleted;

    public DownloadManager()
    {
        Settings = Storage.LoadSettings();
        Limiter.BytesPerSecond = Settings.SpeedLimitKBps * 1024L;
        _items = Storage.LoadDownloads();
        foreach (var item in _items)
            if (item.Status is DownloadStatus.Downloading or DownloadStatus.Merging or DownloadStatus.Queued)
                item.Status = DownloadStatus.Paused;
        _timer = new Timer(_ => Tick(), null, 1000, 1000);
    }

    public IReadOnlyList<DownloadItem> Items
    {
        get { lock (_lock) return _items.ToList(); }
    }

    public bool IsRunning(DownloadItem item)
    {
        lock (_lock) return _running.ContainsKey(item.Id);
    }

    public DownloadItem Add(AddRequest request, string? folder = null, string? fileName = null, bool start = true)
    {
        var kind = string.Equals(request.Kind, "video", StringComparison.OrdinalIgnoreCase) ? DownloadKind.Video : DownloadKind.File;
        string? name = fileName ?? request.FileName;
        var item = new DownloadItem
        {
            Url = request.Url.Trim(),
            Kind = kind,
            FileName = string.IsNullOrWhiteSpace(name) || kind == DownloadKind.Video ? "" : FileNames.Sanitize(Path.GetFileName(name)),
            Folder = folder ?? "",
            Referrer = request.Referrer ?? request.PageUrl,
            Cookies = request.Cookies,
            UserAgent = request.UserAgent,
            Format = kind == DownloadKind.Video && !string.IsNullOrWhiteSpace(request.Format) ? request.Format : null,
            Quality = kind == DownloadKind.Video && !string.IsNullOrWhiteSpace(request.Format) ? request.Quality : null,
            Status = start ? DownloadStatus.Queued : DownloadStatus.Paused,
        };
        lock (_lock) _items.Add(item);
        Save();
        ItemChanged?.Invoke(item);
        Pump();
        return item;
    }

    public void Start(DownloadItem item)
    {
        lock (_lock)
        {
            // A finished download can be started again only if its file is gone (deleted, or never put together).
            if (_running.ContainsKey(item.Id) || (item.Status == DownloadStatus.Completed && File.Exists(item.FullPath))) return;
            item.Status = DownloadStatus.Queued;
            item.Error = null;
        }
        ItemChanged?.Invoke(item);
        Pump();
    }

    public void Pause(DownloadItem item)
    {
        lock (_lock)
        {
            if (_running.TryGetValue(item.Id, out var run)) run.Cts.Cancel();
            else if (item.Status == DownloadStatus.Queued) item.Status = DownloadStatus.Paused;
        }
        ItemChanged?.Invoke(item);
    }

    /// <summary>Throws away progress and downloads the file again from the start.</summary>
    public async Task RestartAsync(DownloadItem item)
    {
        await StopAndWaitAsync(item);
        TryDelete(item.PartPath);
        lock (item.Segments) item.Segments.Clear();
        item.VideoBytes = 0;
        item.TotalSize = -1;
        item.Status = DownloadStatus.Paused;
        Start(item);
    }

    public async Task RemoveAsync(DownloadItem item, bool deleteFile)
    {
        await StopAndWaitAsync(item);
        lock (_lock) _items.Remove(item);
        if (item.Status != DownloadStatus.Completed) TryDelete(item.PartPath);
        else if (deleteFile) TryDelete(item.FullPath);
        Save();
    }

    public void StartAll()
    {
        foreach (var item in Items.Where(i => i.Status is DownloadStatus.Paused or DownloadStatus.Error)) Start(item);
    }

    public void PauseAll()
    {
        foreach (var item in Items.Where(i => i.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Merging)) Pause(item);
    }

    public void ApplySettings(Settings settings)
    {
        Settings = settings.Normalize();
        Storage.SaveSettings(Settings);
        Limiter.BytesPerSecond = Settings.SpeedLimitKBps * 1024L;
        Pump();
    }

    public string FolderFor(DownloadItem item)
    {
        if (!Settings.SortIntoCategories) return Settings.DownloadFolder;
        string category = item.Kind == DownloadKind.Video ? "Video" : FileNames.CategoryFor(item.FileName);
        return Path.Combine(Settings.DownloadFolder, category);
    }

    /// <summary>True if another unfinished download is already writing to this path.</summary>
    bool PathTaken(DownloadItem self, string path)
    {
        lock (_lock)
            return _items.Any(i => i != self && i.Status != DownloadStatus.Completed && i.FileName.Length > 0 &&
                                   string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
    }

    void Pump()
    {
        lock (_lock)
        {
            while (_running.Count < Settings.MaxSimultaneous)
            {
                var next = _items.FirstOrDefault(i => i.Status == DownloadStatus.Queued && !_running.ContainsKey(i.Id));
                if (next == null) break;
                next.Status = DownloadStatus.Downloading;
                var cts = new CancellationTokenSource();
                _running[next.Id] = (cts, Task.Run(() => RunAsync(next, cts.Token)));
            }
        }
    }

    async Task RunAsync(DownloadItem item, CancellationToken ct)
    {
        ItemChanged?.Invoke(item);
        try
        {
            if (item.Kind == DownloadKind.File)
            {
                try
                {
                    await new HttpEngine(item, Settings, Limiter, FolderFor, p => PathTaken(item, p)).RunAsync(ct);
                }
                catch (VideoPageException)
                {
                    item.Kind = DownloadKind.Video;
                    item.FileName = "";
                    item.TotalSize = -1;
                    lock (item.Segments) item.Segments.Clear();
                    ItemChanged?.Invoke(item);
                }
            }
            if (item.Kind == DownloadKind.Video)
                await new VideoEngine(item, Settings, FolderFor).RunAsync(ct);
            item.Status = DownloadStatus.Completed;
            item.Finished = DateTime.Now;
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            item.Status = DownloadStatus.Paused;
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Error;
            item.Error = ex.Message;
        }
        finally
        {
            item.Speed = 0;
            item.ActiveConnections = 0;
            lock (_lock)
            {
                if (_running.Remove(item.Id, out var run)) run.Cts.Dispose();
                _lastBytes.Remove(item.Id);
            }
            Save();
            ItemChanged?.Invoke(item);
            if (item.Status == DownloadStatus.Completed) ItemCompleted?.Invoke(item);
            Pump();
        }
    }

    async Task StopAndWaitAsync(DownloadItem item)
    {
        Task? task = null;
        lock (_lock)
        {
            if (_running.TryGetValue(item.Id, out var run))
            {
                run.Cts.Cancel();
                task = run.Task;
            }
            else if (item.Status == DownloadStatus.Queued)
            {
                item.Status = DownloadStatus.Paused;
            }
        }
        if (task != null) await task;
    }

    void Tick()
    {
        List<DownloadItem> active;
        lock (_lock) active = _items.Where(i => _running.ContainsKey(i.Id)).ToList();

        foreach (var item in active.Where(i => i.Kind == DownloadKind.File))
        {
            long now = item.Downloaded;
            long before;
            lock (_lock) { before = _lastBytes.GetValueOrDefault(item.Id, now); _lastBytes[item.Id] = now; }
            item.Speed = item.Speed * 0.6 + Math.Max(0, now - before) * 0.4; // smoothed bytes per second
        }

        if (active.Count > 0 && ++_ticks % 5 == 0) Save();
    }

    public void Save()
    {
        lock (_saveLock)
        {
            List<DownloadItem> items;
            lock (_lock) items = _items.ToList();
            // Hold every segment list so no range is split while it is being written out.
            var held = new List<object>();
            try
            {
                foreach (var item in items) { Monitor.Enter(item.Segments); held.Add(item.Segments); }
                Storage.SaveDownloads(items);
            }
            catch (IOException) { }
            finally
            {
                foreach (var o in held) Monitor.Exit(o);
            }
        }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _timer.Dispose();
        List<Task> tasks;
        lock (_lock)
        {
            foreach (var run in _running.Values) run.Cts.Cancel();
            tasks = _running.Values.Select(r => r.Task).ToList();
        }
        Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(5));
        Save();
    }
}
