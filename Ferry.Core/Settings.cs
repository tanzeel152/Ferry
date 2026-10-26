namespace Ferry.Core;

public sealed class Settings
{
    /// <summary>"System" (follow Windows), "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    public string DownloadFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Ferry");

    /// <summary>Save files under Video, Music, Compressed, Documents, Programs or General.</summary>
    public bool SortIntoCategories { get; set; } = true;

    public int ConnectionsPerDownload { get; set; } = 8;

    public int MaxSimultaneous { get; set; } = 3;

    /// <summary>Total speed limit in KB/s; 0 means no limit.</summary>
    public int SpeedLimitKBps { get; set; }

    /// <summary>Empty uses the Windows proxy. Example: http://127.0.0.1:8080 or socks5://127.0.0.1:1080</summary>
    public string Proxy { get; set; } = "";

    public int Retries { get; set; } = 5;

    /// <summary>The browser extension connects to http://localhost:&lt;port&gt;. Takes effect after a restart.</summary>
    public int BridgePort { get; set; } = 51515;

    /// <summary>Show a confirmation window for downloads sent by the browser.</summary>
    public bool ConfirmBrowserDownloads { get; set; } = true;

    /// <summary>Offer to download when a link to one of <see cref="CaptureExtensions"/> is copied.</summary>
    public bool MonitorClipboard { get; set; } = true;

    public string CaptureExtensions { get; set; } =
        "zip rar 7z gz tar bz2 xz iso exe msi apk dmg bin mp4 mkv avi mov webm flv wmv m4v mp3 flac wav m4a aac ogg pdf doc docx xls xlsx ppt pptx epub torrent";

    /// <summary>yt-dlp format selector. Default: best video + best audio, merged.</summary>
    public string VideoFormat { get; set; } = "bv*+ba/b";

    public string VideoContainer { get; set; } = "mp4";

    public bool IsCaptureType(string fileOrUrl)
    {
        string ext = Path.GetExtension(fileOrUrl.Split('?', '#')[0]).TrimStart('.').ToLowerInvariant();
        return ext.Length > 0 && CaptureExtensions.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(ext);
    }

    public Settings Normalize()
    {
        if (Theme is not ("System" or "Light" or "Dark")) Theme = "System";
        ConnectionsPerDownload = Math.Clamp(ConnectionsPerDownload, 1, 32);
        MaxSimultaneous = Math.Clamp(MaxSimultaneous, 1, 20);
        SpeedLimitKBps = Math.Max(0, SpeedLimitKBps);
        Retries = Math.Clamp(Retries, 0, 50);
        if (BridgePort is < 1024 or > 65535) BridgePort = 51515;
        if (string.IsNullOrWhiteSpace(VideoFormat)) VideoFormat = "bv*+ba/b";
        if (string.IsNullOrWhiteSpace(VideoContainer)) VideoContainer = "mp4";
        return this;
    }
}
