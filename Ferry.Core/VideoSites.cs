namespace Ferry.Core;

/// <summary>Sites whose pages are downloaded as videos (with yt-dlp) rather than saved as web pages.</summary>
public static class VideoSites
{
    static readonly string[] Hosts =
    {
        "youtube.com", "youtu.be", "vimeo.com", "facebook.com", "fb.watch", "instagram.com", "tiktok.com",
        "twitter.com", "x.com", "dailymotion.com", "twitch.tv", "reddit.com", "soundcloud.com", "bilibili.com", "ok.ru", "vk.com",
    };

    public static bool IsVideoSite(Uri uri)
    {
        string host = uri.Host.ToLowerInvariant();
        return Hosts.Any(h => host == h || host.EndsWith("." + h));
    }

    /// <summary>True for a link to a page on a video site, or to an HLS/DASH stream.</summary>
    public static bool IsVideoLink(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        string path = uri.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".m3u8") || path.EndsWith(".mpd") || IsVideoSite(uri);
    }
}

/// <summary>Thrown when a link added as a file turns out to be a web page on a video site.</summary>
public sealed class VideoPageException() : Exception("This link is a video page, not a file.");
