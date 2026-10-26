namespace Ferry.Core;

public static class FileNames
{
    static readonly Dictionary<string, string[]> Categories = new()
    {
        ["Compressed"] = new[] { "zip", "rar", "7z", "gz", "tar", "bz2", "xz", "tgz", "iso", "cab" },
        ["Documents"] = new[] { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "rtf", "epub", "odt", "csv" },
        ["Music"] = new[] { "mp3", "flac", "wav", "m4a", "aac", "ogg", "wma", "opus" },
        ["Programs"] = new[] { "exe", "msi", "apk", "dmg", "msix", "appx", "bat", "jar" },
        ["Video"] = new[] { "mp4", "mkv", "avi", "mov", "webm", "flv", "wmv", "m4v", "ts", "3gp", "mpg", "mpeg" },
    };

    static readonly Dictionary<string, string> MimeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/zip"] = ".zip", ["application/pdf"] = ".pdf", ["application/x-msdownload"] = ".exe",
        ["application/x-rar-compressed"] = ".rar", ["application/x-7z-compressed"] = ".7z",
        ["video/mp4"] = ".mp4", ["video/webm"] = ".webm", ["video/x-matroska"] = ".mkv",
        ["audio/mpeg"] = ".mp3", ["audio/mp4"] = ".m4a", ["image/jpeg"] = ".jpg", ["image/png"] = ".png",
        ["text/plain"] = ".txt", ["application/json"] = ".json", ["text/html"] = ".html",
    };

    public static string CategoryFor(string fileName)
    {
        string ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        foreach (var (name, exts) in Categories)
            if (exts.Contains(ext)) return name;
        return "General";
    }

    /// <summary>Makes a name safe for Windows: no invalid characters, no reserved names, sensible length.</summary>
    public static string Sanitize(string name)
    {
        name = Uri.UnescapeDataString(name ?? "").Trim().Trim('.', ' ');
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (name.Length == 0) name = "download";
        string stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
            name = "_" + name;
        if (name.Length > 180)
        {
            string ext = Path.GetExtension(name);
            name = name[..(180 - ext.Length)] + ext;
        }
        return name;
    }

    public static string FromUrl(Uri uri, string? contentType)
    {
        string last = uri.Segments.Length > 0 ? uri.Segments[^1].Trim('/') : "";
        string name = Sanitize(string.IsNullOrEmpty(last) ? uri.Host : last);
        if (!Path.HasExtension(name) && contentType != null && MimeExtensions.TryGetValue(contentType.Split(';')[0].Trim(), out var ext))
            name += ext;
        return name;
    }

    /// <summary>Returns "name.ext", or "name (1).ext", "name (2).ext"... if the file (or its partial file) already exists.</summary>
    public static string Unique(string folder, string fileName, Func<string, bool>? taken = null)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName), ext = Path.GetExtension(fileName);
        string candidate = fileName;
        for (int i = 1; IsTaken(candidate); i++) candidate = $"{stem} ({i}){ext}";
        return candidate;

        bool IsTaken(string n)
        {
            string p = Path.Combine(folder, n);
            return File.Exists(p) || File.Exists(p + ".ferry") || (taken?.Invoke(p) ?? false);
        }
    }
}
