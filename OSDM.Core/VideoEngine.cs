using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OSDM.Core;

/// <summary>
/// Downloads video pages (YouTube and ~1800 other sites) and HLS/DASH manifests by running yt-dlp,
/// which uses FFmpeg to merge the separate video and audio streams. Pausing kills the process;
/// yt-dlp keeps its .part files, so starting again resumes.
/// </summary>
public sealed class VideoEngine
{
    const string ProgressTag = "OSDMPROG|";
    const string NameTag = "OSDMNAME|";
    const string FileTag = "OSDMFILE|";

    readonly DownloadItem _item;
    readonly Settings _settings;
    readonly Func<DownloadItem, string> _folderFor;

    public VideoEngine(DownloadItem item, Settings settings, Func<DownloadItem, string> folderFor)
    {
        _item = item;
        _settings = settings;
        _folderFor = folderFor;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var item = _item;
        string ytdlp = Tools.YtDlp ?? throw new InvalidOperationException("yt-dlp is not installed. Use Tools > Install video tools.");
        // Formats like "bv*+ba" are separate video and sound streams that FFmpeg joins. Without a working FFmpeg,
        // yt-dlp leaves them as two files and still reports success, so check first.
        bool merging = FormatFor(item).Contains('+');
        string? ffmpeg = Tools.FFmpeg;
        if (merging && Tools.FFmpegProblem() is { } problem) throw new InvalidOperationException(problem);
        if (string.IsNullOrWhiteSpace(item.Folder)) item.Folder = _folderFor(item);
        Directory.CreateDirectory(item.Folder);

        // The browser's cookies are left out at first: with YouTube sign-in cookies, yt-dlp switches to players
        // that need a JavaScript runtime and finds no formats. They are sent only if the site asks to sign in.
        string? error = await RunYtDlpAsync(ytdlp, ffmpeg, cookies: false, ct);
        if (error != null && AsksToSignIn(error) && !string.IsNullOrEmpty(item.Cookies))
            error = await RunYtDlpAsync(ytdlp, ffmpeg, cookies: true, ct);
        if (error != null) throw new InvalidOperationException(error);

        string full = item.FullPath;
        if (!File.Exists(full))
            throw new InvalidOperationException(merging
                ? "The video and sound were downloaded but not joined, because FFmpeg did not run. Press Resume to try again."
                : "yt-dlp finished, but the video file was not found.");
        item.TotalSize = item.VideoBytes = new FileInfo(full).Length;
    }

    string FormatFor(DownloadItem item) => string.IsNullOrWhiteSpace(item.Format) ? _settings.VideoFormat : item.Format;

    internal static bool AsksToSignIn(string error) =>
        new[] { "cookies", "sign in", "log in", "login" }.Any(w => error.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>Options yt-dlp needs to fetch a page the way the browser did (proxy, referrer, user agent, cookies).</summary>
    internal static List<string> BrowserOptions(Settings settings, string? referrer, string? userAgent, string? cookies)
    {
        var a = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.Proxy)) { a.Add("--proxy"); a.Add(settings.Proxy); }
        if (!string.IsNullOrEmpty(referrer)) { a.Add("--referer"); a.Add(referrer); }
        if (!string.IsNullOrEmpty(userAgent)) { a.Add("--user-agent"); a.Add(userAgent); }
        if (!string.IsNullOrEmpty(cookies)) { a.Add("--add-header"); a.Add("Cookie:" + cookies); }
        return a;
    }

    /// <summary>Runs yt-dlp once. Returns null if it succeeded, otherwise its error message.</summary>
    async Task<string?> RunYtDlpAsync(string ytdlp, string? ffmpeg, bool cookies, CancellationToken ct)
    {
        var item = _item;
        var psi = new ProcessStartInfo(ytdlp)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var arg in BuildArguments(ffmpeg, cookies)) psi.ArgumentList.Add(arg);

        using var process = Tools.Start(psi);
        using var kill = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });

        string? lastError = null;
        var stderr = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
                if (line.StartsWith("ERROR:", StringComparison.Ordinal)) lastError = line[6..].Trim();
        });

        long baseBytes = 0, lastDone = 0, lastTotal = 0;
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (line.StartsWith(NameTag, StringComparison.Ordinal))
            {
                item.FileName = FileNames.Sanitize(line[NameTag.Length..]) + "." + _settings.VideoContainer;
            }
            else if (line.StartsWith(FileTag, StringComparison.Ordinal))
            {
                string path = line[FileTag.Length..].Trim();
                item.Folder = Path.GetDirectoryName(path) ?? item.Folder;
                item.FileName = Path.GetFileName(path);
            }
            else if (line.StartsWith(ProgressTag, StringComparison.Ordinal))
            {
                var f = line[ProgressTag.Length..].Split('|');
                if (f.Length < 5) continue;
                string state = f[0];
                long done = Num(f[1]), total = Num(f[2]) > 0 ? Num(f[2]) : Num(f[3]);
                if (done < lastDone) baseBytes += lastTotal; // the next stream (e.g. audio after video) started
                lastDone = done;
                lastTotal = Math.Max(total, done);
                item.VideoBytes = baseBytes + done;
                item.TotalSize = baseBytes + lastTotal;
                item.Speed = Num(f[4]);
                item.Status = state == "finished" ? DownloadStatus.Merging : DownloadStatus.Downloading;
            }
        }

        await process.WaitForExitAsync(CancellationToken.None);
        await stderr;
        ct.ThrowIfCancellationRequested();
        return process.ExitCode == 0 ? null : lastError ?? $"yt-dlp failed (exit code {process.ExitCode}).";
    }

    IEnumerable<string> BuildArguments(string? ffmpeg, bool cookies)
    {
        var item = _item;
        var a = new List<string>
        {
            // UTF-8 output: otherwise characters such as the "｜" yt-dlp puts in file names are dropped from the
            // printed path, and the finished file is not found.
            "--encoding", "utf-8",
            "--newline", "--no-colors", "--no-playlist", "--quiet", "--progress", "--no-simulate",
            "-f", FormatFor(item),
            "--merge-output-format", _settings.VideoContainer,
            "-P", item.Folder,
            "-o", "%(title).150B [%(id)s].%(ext)s",
            "--progress-template",
            "download:" + ProgressTag + "%(progress.status)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s",
            "--print", "before_dl:" + NameTag + "%(title).150B [%(id)s]",
            "--print", "after_move:" + FileTag + "%(filepath)s",
        };
        if (ffmpeg != null) { a.Add("--ffmpeg-location"); a.Add(Path.GetDirectoryName(ffmpeg)!); }
        a.AddRange(BrowserOptions(_settings, item.Referrer, item.UserAgent, cookies ? item.Cookies : null));
        if (_settings.SpeedLimitKBps > 0) { a.Add("--limit-rate"); a.Add(_settings.SpeedLimitKBps + "K"); }
        a.Add("--");
        a.Add(item.Url);
        return a;
    }

    static long Num(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (long)v : 0;
}
