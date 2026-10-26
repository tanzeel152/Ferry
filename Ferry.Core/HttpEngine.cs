using System.Buffers;
using System.Net;
using Microsoft.Win32.SafeHandles;

namespace Ferry.Core;

/// <summary>
/// Multi-connection HTTP downloader. The file is split into byte ranges, each fetched by its own
/// connection and written straight to its offset in "name.ext.ferry". When a connection finishes early it
/// takes over half of the biggest remaining range (dynamic segmentation), so all connections stay busy.
/// </summary>
public sealed class HttpEngine
{
    const long MinSplitBytes = 512 * 1024;
    static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    readonly DownloadItem _item;
    readonly Settings _settings;
    readonly SpeedLimiter _limiter;
    readonly Func<DownloadItem, string> _folderFor;
    readonly Func<string, bool> _pathTaken;

    public HttpEngine(DownloadItem item, Settings settings, SpeedLimiter limiter,
        Func<DownloadItem, string> folderFor, Func<string, bool> pathTaken)
    {
        _item = item;
        _settings = settings;
        _limiter = limiter;
        _folderFor = folderFor;
        _pathTaken = pathTaken;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var item = _item;
        bool fresh = item.Segments.Count == 0 || !File.Exists(item.PartPath) || (!item.ResumeSupported && item.Downloaded > 0);
        if (fresh) await ProbeAsync(ct);

        Directory.CreateDirectory(item.Folder);
        string partPath = item.PartPath;
        using (var fs = new FileStream(partPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 1))
        {
            if (fresh || !item.ResumeSupported) fs.SetLength(0);
            if (item.TotalSize > 0 && fs.Length != item.TotalSize) fs.SetLength(item.TotalSize);

            await RunWorkersAsync(fs.SafeFileHandle, ct);
            fs.Flush(true);
            if (item.TotalSize < 0) item.TotalSize = item.Downloaded;
        }

        if (File.Exists(item.FullPath)) item.FileName = FileNames.Unique(item.Folder, item.FileName, _pathTaken);
        File.Move(partPath, item.FullPath);
    }

    async Task ProbeAsync(CancellationToken ct)
    {
        var item = _item;
        using var resp = await Net.SendAsync(item, _settings.Proxy, 0, null, false, ct);
        resp.EnsureSuccessStatusCode();
        // A YouTube (or similar) page sent as a file would otherwise be saved as "watch.html".
        if (resp.Content.Headers.ContentType?.MediaType == "text/html" && VideoSites.IsVideoSite(resp.RequestMessage?.RequestUri ?? new Uri(item.Url)))
            throw new VideoPageException();

        long total = -1;
        bool ranges = false;
        if (resp.StatusCode == HttpStatusCode.PartialContent && resp.Content.Headers.ContentRange is { HasLength: true } cr)
        {
            total = cr.Length!.Value;
            ranges = true;
        }
        else if (resp.Content.Headers.ContentLength is long len)
        {
            total = len;
        }

        item.TotalSize = total;
        item.ResumeSupported = ranges && total > 0;
        item.ETag = resp.Headers.ETag is { IsWeak: false } tag ? tag.Tag : null;
        item.LastModified = resp.Content.Headers.LastModified?.ToString("R");

        if (string.IsNullOrWhiteSpace(item.FileName))
        {
            var cd = resp.Content.Headers.ContentDisposition;
            string? name = cd?.FileNameStar ?? cd?.FileName?.Trim('"');
            item.FileName = !string.IsNullOrWhiteSpace(name)
                ? FileNames.Sanitize(name)
                : FileNames.FromUrl(resp.RequestMessage?.RequestUri ?? new Uri(item.Url), resp.Content.Headers.ContentType?.MediaType);
        }
        if (string.IsNullOrWhiteSpace(item.Folder)) item.Folder = _folderFor(item);
        if (!File.Exists(item.PartPath)) item.FileName = FileNames.Unique(item.Folder, item.FileName, _pathTaken);

        lock (item.Segments)
        {
            item.Segments.Clear();
            item.Segments.Add(new Segment { Start = 0, End = total > 0 ? total - 1 : -1 });
        }
    }

    async Task RunWorkersAsync(SafeFileHandle file, CancellationToken ct)
    {
        var item = _item;
        int connections = item.ResumeSupported ? _settings.ConnectionsPerDownload : 1;
        var running = new Dictionary<Task, Segment>();
        var failed = new HashSet<Segment>();
        Exception? lastError = null;

        void Launch(Segment seg) => running[Task.Run(() => WorkerAsync(seg, file, ct), ct)] = seg;

        Segment? NextIdle()
        {
            lock (item.Segments)
                return item.Segments.FirstOrDefault(s => !s.Done && !failed.Contains(s) && !running.ContainsValue(s));
        }

        while (running.Count < connections && NextIdle() is { } idle) Launch(idle);
        while (running.Count < connections && TrySplit(running.Values, out var piece)) Launch(piece);

        while (running.Count > 0)
        {
            item.ActiveConnections = running.Count;
            var finished = await Task.WhenAny(running.Keys);
            var seg = running[finished];
            running.Remove(finished);

            if (finished.IsFaulted)
            {
                lastError = finished.Exception!.InnerException;
                failed.Add(seg);
            }
            if (ct.IsCancellationRequested) continue;

            if (NextIdle() is { } next) Launch(next);
            else if (finished.IsCompletedSuccessfully && TrySplit(running.Values, out var piece)) Launch(piece);
        }
        item.ActiveConnections = 0;

        ct.ThrowIfCancellationRequested();
        lock (item.Segments)
            if (item.Segments.Any(s => !s.Done))
                throw lastError ?? new IOException("The download did not finish.");
    }

    /// <summary>Gives the second half of the largest active range to a new connection.</summary>
    bool TrySplit(IEnumerable<Segment> active, out Segment piece)
    {
        piece = null!;
        if (!_item.ResumeSupported) return false;
        var biggest = active.Where(s => s.End >= 0).OrderByDescending(s => s.Remaining).FirstOrDefault();
        if (biggest == null) return false;

        // Shrink and add under the list lock, so a save never sees the shrunk range without its new half.
        lock (_item.Segments)
        {
            lock (biggest)
            {
                long remaining = biggest.End - biggest.Position + 1;
                if (remaining < 2 * MinSplitBytes) return false;
                long mid = biggest.Position + remaining / 2;
                piece = new Segment { Start = mid, End = biggest.End };
                biggest.End = mid - 1;
            }
            _item.Segments.Add(piece);
        }
        return true;
    }

    async Task WorkerAsync(Segment seg, SafeFileHandle file, CancellationToken ct)
    {
        var item = _item;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        int attempt = 0;
        try
        {
            while (!seg.Done)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    long from, to;
                    lock (seg) { from = seg.Position; to = seg.End; }
                    bool multi = item.Segments.Count > 1;
                    using var resp = await Net.SendAsync(item, _settings.Proxy,
                        item.ResumeSupported || from > 0 ? from : null, to >= 0 ? to : null, true, ct);
                    resp.EnsureSuccessStatusCode();

                    if (from > 0 && resp.StatusCode != HttpStatusCode.PartialContent)
                    {
                        // Range ignored, or If-Range failed because the file changed on the server.
                        if (multi) throw new InvalidOperationException("The server no longer accepts resume for this file. Restart the download.");
                        lock (seg) seg.Downloaded = 0;
                    }

                    await using var body = await resp.Content.ReadAsStreamAsync(ct);
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    while (true)
                    {
                        readCts.CancelAfter(ReadTimeout);
                        int n = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), readCts.Token);
                        if (n == 0) break;
                        await _limiter.WaitAsync(n, ct);

                        bool segmentDone;
                        lock (seg)
                        {
                            long pos = seg.Position;
                            long allowed = seg.End < 0 ? n : Math.Min(n, seg.End - pos + 1);
                            if (allowed > 0)
                            {
                                RandomAccess.Write(file, buffer.AsSpan(0, (int)allowed), pos);
                                seg.Downloaded += allowed;
                            }
                            segmentDone = seg.Done;
                        }
                        attempt = 0;
                        if (segmentDone) return; // also true when another connection took our second half
                    }

                    if (seg.End < 0)
                    {
                        lock (seg) seg.End = seg.Start + seg.Downloaded - 1; // unknown size: end of stream = done
                        return;
                    }
                    if (!seg.Done) throw new IOException("The connection closed early.");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && Net.IsRetryable(ex) && attempt < _settings.Retries)
                {
                    attempt++;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * attempt)), ct);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
