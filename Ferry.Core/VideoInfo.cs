using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ferry.Core;

/// <summary>One entry in the quality menu: "1080p", "MP4 · 71.4 MB", and the yt-dlp format that downloads it.</summary>
public sealed record VideoQuality(string Label, string Detail, string Format, long Size);

/// <summary>A video page's title and the qualities it can be downloaded in, read with yt-dlp.</summary>
public sealed class VideoInfo
{
    public string Title { get; init; } = "";
    public List<VideoQuality> Qualities { get; init; } = new();

    /// <summary>Choices offered in Settings and in the add window, where the page's own list is not known.</summary>
    public static readonly (string Label, string Format)[] Presets =
    {
        ("Best available", "bv*+ba/b"),
        ("Up to 1080p", "bv*[height<=1080]+ba/b[height<=1080]"),
        ("Up to 720p", "bv*[height<=720]+ba/b[height<=720]"),
        ("Up to 480p", "bv*[height<=480]+ba/b[height<=480]"),
        ("Audio only", "ba/b"),
    };

    static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(10);
    static readonly Dictionary<string, (DateTime Time, Task<VideoInfo> Task)> Cache = new();

    /// <summary>Reads the page's qualities. Results are kept for a few minutes, so opening the menu again is instant.</summary>
    public static Task<VideoInfo> GetAsync(AddRequest request, Settings settings)
    {
        lock (Cache)
        {
            foreach (var old in Cache.Where(c => DateTime.Now - c.Value.Time > CacheTime || c.Value.Task.IsFaulted).Select(c => c.Key).ToList())
                Cache.Remove(old);
            if (!Cache.TryGetValue(request.Url, out var entry))
                Cache[request.Url] = entry = (DateTime.Now, Task.Run(() => FetchAsync(request, settings)));
            return entry.Task;
        }
    }

    static async Task<VideoInfo> FetchAsync(AddRequest request, Settings settings)
    {
        if (Tools.YtDlp == null) throw new InvalidOperationException("The video tools are not installed. In Ferry, use Tools > Install video tools.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        // Like downloads: without the browser's cookies first (see VideoEngine), with them only if the site asks to sign in.
        var (json, error) = await RunAsync(request, settings, cookies: false, timeout.Token);
        if (json == null && VideoEngine.AsksToSignIn(error!) && !string.IsNullOrEmpty(request.Cookies))
            (json, error) = await RunAsync(request, settings, cookies: true, timeout.Token);
        if (json == null) throw new InvalidOperationException(error);
        return Parse(json, settings.VideoContainer);
    }

    static async Task<(string? Json, string? Error)> RunAsync(AddRequest request, Settings settings, bool cookies, CancellationToken ct)
    {
        var psi = Tools.YtDlpCommand() ?? throw new InvalidOperationException("The video tools are not installed. In Ferry, use Tools > Install video tools.");
        foreach (var arg in new[] { "--dump-single-json", "--no-playlist", "--no-warnings" })
            psi.ArgumentList.Add(arg);
        foreach (var arg in VideoEngine.BrowserOptions(settings, request.Referrer ?? request.PageUrl, request.UserAgent, cookies ? request.Cookies : null))
            psi.ArgumentList.Add(arg);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(request.Url);

        using var process = Tools.Start(psi);
        using var kill = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None);
        string output = await stdout, errors = await stderr;

        if (ct.IsCancellationRequested) return (null, "yt-dlp took too long to read this page.");
        if (process.ExitCode == 0 && output.TrimStart().StartsWith('{')) return (output, null);
        string? last = errors.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("ERROR:", StringComparison.Ordinal));
        return (null, last?[6..].Trim() ?? $"yt-dlp could not read this page (exit code {process.ExitCode}).");
    }

    /// <summary>
    /// Turns yt-dlp's JSON into menu entries: the best stream for each resolution (H.264 first, as it plays everywhere;
    /// direct downloads before HLS), joined with the best sound, then "Audio only".
    /// </summary>
    public static VideoInfo Parse(string json, string container)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        double duration = Number(root, "duration");
        var formats = root.TryGetProperty("formats", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(e => Stream.From(e, duration)).ToList()
            : new List<Stream> { Stream.From(root, duration) }; // pages with one format describe it on the root

        var audio = formats.Where(f => f.HasAudio && !f.HasVideo)
            .OrderByDescending(f => f.LanguagePreference)
            .ThenBy(f => f.Ext == "m4a" ? 0 : 1)
            .ThenBy(f => f.Direct ? 0 : 1)
            .ThenByDescending(f => f.Bitrate)
            .FirstOrDefault();

        var qualities = new List<VideoQuality>();
        var groups = formats.Where(f => f.HasVideo && f.Lines > 0)
            .GroupBy(f => f.Label)
            .Select(g => g.OrderBy(f => f.Direct ? 0 : 1).ThenBy(f => f.CodecRank).ThenByDescending(f => f.Bitrate).First())
            .OrderByDescending(f => f.Lines).ThenByDescending(f => f.Fps)
            .Take(10);
        foreach (var v in groups)
        {
            bool join = !v.HasAudio && audio != null;
            long size = v.Size + (join ? audio!.Size : 0);
            string ext = join ? container : v.Ext;
            // Fallbacks in case the exact streams are gone by the time the download starts.
            string fallback = v.Height > 0 ? $"/bv*[height<={v.Height}]+ba/b[height<={v.Height}]/b" : "/b";
            qualities.Add(new VideoQuality(v.Label, Detail(ext, size), (join ? $"{v.Id}+{audio!.Id}" : v.Id) + fallback, size));
        }
        if (audio != null)
            qualities.Add(new VideoQuality("Audio only", Detail(audio.Ext, audio.Size), audio.Id + "/ba/b", audio.Size));
        if (qualities.Count == 0)
            qualities.Add(new VideoQuality("Best available", "", "bv*+ba/b", 0));

        return new VideoInfo { Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "", Qualities = qualities };
    }

    static string Detail(string ext, long size)
    {
        string type = ext.ToUpperInvariant();
        if (size <= 0) return type;
        string amount = size >= 1L << 30
            ? (size / (double)(1L << 30)).ToString("0.00", CultureInfo.InvariantCulture) + " GB"
            : (size / (double)(1L << 20)).ToString(size >= 100L << 20 ? "0" : "0.0", CultureInfo.InvariantCulture) + " MB";
        return $"{type} · {amount}";
    }

    static double Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>The parts of one yt-dlp format that matter for choosing.</summary>
    sealed record Stream(string Id, string Ext, bool HasVideo, bool HasAudio, bool Direct, int CodecRank,
        int Width, int Height, double Fps, long Size, double Bitrate, int LanguagePreference, int Lines, string Label)
    {
        static readonly Regex NoteLines = new(@"^(\d{3,4})p", RegexOptions.Compiled);

        public static Stream From(JsonElement e, double duration)
        {
            string vcodec = Text(e, "vcodec"), acodec = Text(e, "acodec"), ext = Text(e, "ext"), protocol = Text(e, "protocol");
            int width = (int)Number(e, "width"), height = (int)Number(e, "height");
            double fps = Number(e, "fps"), bitrate = Number(e, "tbr") is > 0 and var tbr ? tbr : Number(e, "abr");
            long size = (long)(Number(e, "filesize") is > 0 and var exact ? exact
                : Number(e, "filesize_approx") is > 0 and var approx ? approx
                : bitrate * 1000 / 8 * duration);
            // yt-dlp says "none" for a missing stream and leaves the codec out when it does not know it.
            bool hasVideo = vcodec != "none" && (vcodec.Length > 0 || height > 0) && ext != "mhtml";
            bool hasAudio = acodec != "none" && ext != "mhtml" && (acodec.Length > 0 || hasVideo || Number(e, "abr") > 0);

            // "1080p" for 1920x1080, but also for wide (1920x742) and tall (1080x1920) videos.
            var note = NoteLines.Match(Text(e, "format_note"));
            int lines = note.Success ? int.Parse(note.Groups[1].Value, CultureInfo.InvariantCulture)
                : width > 0 && height > 0 ? Math.Max(Math.Min(width, height), (int)Math.Round(Math.Max(width, height) * 9 / 16.0))
                : height;
            string label = $"{lines}p" + (fps >= 48 ? Math.Round(fps).ToString(CultureInfo.InvariantCulture) : "") +
                           (Text(e, "dynamic_range") is { Length: > 0 } range && range != "SDR" ? " HDR" : "");

            int codecRank = vcodec.StartsWith("avc1") || vcodec.StartsWith("h264") ? 0 : vcodec.StartsWith("av01") ? 1 : vcodec.StartsWith("vp") ? 2 : 3;
            bool direct = protocol is "https" or "http" or "";
            return new Stream(Text(e, "format_id"), ext, hasVideo, hasAudio, direct, codecRank, width, height, fps, Math.Max(0, size),
                bitrate, (int)Number(e, "language_preference"), hasVideo ? lines : 0, label);
        }
    }
}
