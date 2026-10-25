using OSDM.Core;

namespace OSDM;

static class Format
{
    public static string Size(long bytes) => bytes switch
    {
        < 0 => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:F1} MB",
        _ => $"{bytes / 1073741824.0:F2} GB",
    };

    public static string Speed(double bytesPerSecond) => bytesPerSecond < 1 ? "" : Size((long)bytesPerSecond) + "/s";

    public static string TimeLeft(DownloadItem item)
    {
        if (item.Status != DownloadStatus.Downloading || item.Speed < 1 || item.TotalSize <= 0) return "";
        var t = TimeSpan.FromSeconds(Math.Max(0, item.TotalSize - item.Downloaded) / item.Speed);
        if (t.TotalHours >= 24) return "> 1 day";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    public static string Status(DownloadItem item) => item.Status switch
    {
        DownloadStatus.Downloading when item.Kind == DownloadKind.Video => "Downloading video",
        DownloadStatus.Downloading when item.ActiveConnections > 0 => $"Downloading ({item.ActiveConnections} connections)",
        DownloadStatus.Downloading => "Connecting...",
        DownloadStatus.Merging => "Merging video and audio...",
        DownloadStatus.Error => "Error: " + item.Error,
        DownloadStatus.Completed => "Complete",
        var s => s.ToString(),
    };

    /// <summary>"Today, 13:07", "Yesterday, 09:15", "24 Oct, 01:07", or "24 Oct 2025" for older years.</summary>
    public static string Date(DateTime when)
    {
        var today = DateTime.Today;
        if (when.Date == today) return "Today, " + when.ToString("HH:mm");
        if (when.Date == today.AddDays(-1)) return "Yesterday, " + when.ToString("HH:mm");
        return when.Year == today.Year ? when.ToString("d MMM, HH:mm") : when.ToString("d MMM yyyy");
    }

    public static string DisplayName(DownloadItem item) =>
        item.FileName.Length > 0 ? item.FileName : item.Url;
}
