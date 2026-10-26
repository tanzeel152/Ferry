using System.Text.Json.Serialization;

namespace Ferry.Core;

public enum DownloadStatus { Queued, Downloading, Paused, Merging, Completed, Error }

public enum DownloadKind { File, Video }

/// <summary>One byte range of a file, downloaded by one connection.</summary>
public sealed class Segment
{
    public long Start { get; set; }

    /// <summary>Last byte (inclusive). -1 when the size is unknown and the file is one stream.</summary>
    public long End { get; set; }

    public long Downloaded { get; set; }

    [JsonIgnore] public long Length => End < 0 ? -1 : End - Start + 1;
    [JsonIgnore] public long Position => Start + Downloaded;
    [JsonIgnore] public long Remaining => End < 0 ? long.MaxValue : Length - Downloaded;
    [JsonIgnore] public bool Done => End >= 0 && Downloaded >= Length;
}

public sealed class DownloadItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = "";
    public DownloadKind Kind { get; set; }
    public string FileName { get; set; } = "";
    public string Folder { get; set; } = "";
    public long TotalSize { get; set; } = -1;
    public bool ResumeSupported { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public string? Error { get; set; }

    // Context from the browser, sent with every request.
    public string? Referrer { get; set; }
    public string? Cookies { get; set; }
    public string? UserAgent { get; set; }

    // Used to check the file did not change on the server between pause and resume.
    public string? ETag { get; set; }
    public string? LastModified { get; set; }

    public List<Segment> Segments { get; set; } = new();
    public DateTime Added { get; set; } = DateTime.Now;
    public DateTime? Finished { get; set; }

    /// <summary>Bytes done for video downloads (reported by yt-dlp).</summary>
    public long VideoBytes { get; set; }

    /// <summary>yt-dlp format chosen for this video, and its name ("1080p"). Null: the default from Settings.</summary>
    public string? Format { get; set; }
    public string? Quality { get; set; }

    [JsonIgnore] public double Speed { get; set; }
    [JsonIgnore] public int ActiveConnections { get; set; }

    [JsonIgnore]
    public long Downloaded
    {
        get
        {
            if (Kind == DownloadKind.Video) return VideoBytes;
            long sum = 0;
            lock (Segments) foreach (var s in Segments) sum += s.Downloaded;
            return sum;
        }
    }

    [JsonIgnore] public double Progress => TotalSize > 0 ? Math.Min(1.0, (double)Downloaded / TotalSize) : 0;
    [JsonIgnore] public string FullPath => Path.Combine(Folder, FileName);
    [JsonIgnore] public string PartPath => FullPath + ".ferry";
}

/// <summary>A download handed to Ferry by the browser extension, the command line or the clipboard.</summary>
public sealed class AddRequest
{
    public string Url { get; set; } = "";
    public string? FileName { get; set; }
    public string? Referrer { get; set; }
    public string? Cookies { get; set; }
    public string? UserAgent { get; set; }
    public string? PageUrl { get; set; }
    public string? Kind { get; set; }   // "file" or "video"
    public string? Format { get; set; } // videos: yt-dlp format picked from the quality menu
    public string? Quality { get; set; }
}
