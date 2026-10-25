using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace OSDM.Core;

/// <summary>Finds or installs the open-source video tools: yt-dlp (site extraction) and FFmpeg (merging).</summary>
public static class Tools
{
    const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    const string FFmpegZipUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OSDM", "tools");

    public static string? YtDlp => Find("yt-dlp.exe");
    public static string? FFmpeg => Find("ffmpeg.exe");
    public static bool Ready => YtDlp != null && FFmpeg != null;

    /// <summary>
    /// Starts yt-dlp or FFmpeg with Windows error message boxes turned off (child processes inherit this).
    /// Smart App Control blocks a library inside yt-dlp.exe (Cryptodome\Hash\_SHA1.pyd). yt-dlp works without it,
    /// but Windows would show a "Bad Image" box and yt-dlp would wait until someone clicked OK.
    /// </summary>
    public static Process Start(ProcessStartInfo psi)
    {
        SetErrorMode(GetErrorMode() | SEM_FAILCRITICALERRORS);
        return Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(psi.FileName)}.");
    }

    const uint SEM_FAILCRITICALERRORS = 0x0001;
    [DllImport("kernel32.dll")] static extern uint GetErrorMode();
    [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);

    /// <summary>
    /// Null if FFmpeg starts, otherwise why not. Windows Smart App Control can refuse to run a program it has
    /// not finished checking ("An Application Control policy has blocked this file").
    /// </summary>
    public static string? FFmpegProblem()
    {
        string? ffmpeg = FFmpeg;
        if (ffmpeg == null) return "FFmpeg is not installed. Use Tools > Install video tools.";
        try
        {
            using var p = Start(new ProcessStartInfo(ffmpeg, "-hide_banner -version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? null : $"FFmpeg did not start (exit code {p.ExitCode}).";
        }
        catch (Win32Exception ex)
        {
            return $"Windows did not let FFmpeg run ({ex.Message.TrimEnd('.')}), so the video and sound cannot be joined. Press Resume to try again.";
        }
    }

    static string? Find(string exe)
    {
        var dirs = new List<string> { AppContext.BaseDirectory, Folder };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (var dir in dirs)
        {
            try
            {
                string p = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    /// <summary>Downloads whichever of yt-dlp.exe and ffmpeg.exe is missing into %LOCALAPPDATA%\OSDM\tools.</summary>
    public static async Task InstallAsync(IProgress<string> status, CancellationToken ct)
    {
        Directory.CreateDirectory(Folder);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OSDM/0.1");

        if (YtDlp == null)
            await FetchAsync(http, YtDlpUrl, Path.Combine(Folder, "yt-dlp.exe"), "yt-dlp", status, ct);

        if (FFmpeg == null)
        {
            string zip = Path.Combine(Folder, "ffmpeg.zip");
            await FetchAsync(http, FFmpegZipUrl, zip, "FFmpeg", status, ct);
            status.Report("Unpacking FFmpeg...");
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ||
                        entry.FullName.EndsWith("/bin/ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                        entry.ExtractToFile(Path.Combine(Folder, entry.Name), true);
                }
            }
            File.Delete(zip);
        }
        status.Report(Ready ? "Video tools are ready." : "Video tools could not be installed.");
    }

    static async Task FetchAsync(HttpClient http, string url, string target, string label, IProgress<string> status, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        string tmp = target + ".part";
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmp))
        {
            var buffer = new byte[128 * 1024];
            long done = 0, lastReport = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                if (done - lastReport > 1_000_000)
                {
                    lastReport = done;
                    status.Report(total > 0 ? $"Downloading {label}: {done * 100 / total}%" : $"Downloading {label}: {done / 1_048_576} MB");
                }
            }
        }
        File.Move(tmp, target, true);
    }
}
