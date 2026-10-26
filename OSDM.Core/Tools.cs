using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace OSDM.Core;

/// <summary>Finds or installs the open-source video tools: yt-dlp (site extraction) and FFmpeg (merging).</summary>
/// <remarks>
/// yt-dlp is installed as its plain-Python version (yt-dlp.pyz) run by the official embeddable Python, not as
/// yt-dlp.exe. Every program file of that Python is signed by the Python Software Foundation, so Windows Smart App
/// Control lets it run. yt-dlp.exe is unsigned and unpacks unsigned libraries each run, which Smart App Control
/// blocks (with a Windows Security notification) until it has seen them often enough, and again after each update.
/// </remarks>
public static class Tools
{
    const string PythonVersion = "3.13.15";
    const string PythonZipUrl = "https://www.python.org/ftp/python/" + PythonVersion + "/python-" + PythonVersion + "-embed-amd64.zip";
    const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp";
    const string FFmpegZipUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OSDM", "tools");

    static string PythonExe => Path.Combine(Folder, "python", "python.exe");
    static string YtDlpPyz => Path.Combine(Folder, "yt-dlp.pyz");

    public static string? YtDlp => File.Exists(PythonExe) && File.Exists(YtDlpPyz) ? YtDlpPyz : null;
    public static string? FFmpeg => Find("ffmpeg.exe");
    public static bool Ready => YtDlp != null && FFmpeg != null;

    /// <summary>True for installs from OSDM 0.3 and older, which used yt-dlp.exe; installing again replaces it.</summary>
    public static bool HasOldYtDlp => YtDlp == null && File.Exists(Path.Combine(Folder, "yt-dlp.exe"));

    /// <summary>
    /// A yt-dlp command (UTF-8 output, no window) to add arguments to and run with <see cref="Start"/>.
    /// Null if yt-dlp is not installed.
    /// </summary>
    public static ProcessStartInfo? YtDlpCommand()
    {
        if (YtDlp is not { } pyz) return null;
        var psi = new ProcessStartInfo(PythonExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // UTF-8 output: otherwise characters such as the "｜" yt-dlp puts in file names are dropped from the
        // printed path, and the finished file is not found.
        foreach (var arg in new[] { "-X", "utf8", pyz, "--encoding", "utf-8", "--no-colors" }) psi.ArgumentList.Add(arg);
        // YouTube's player scripts need a JavaScript runtime. Without one, yt-dlp finds no formats when signed in
        // (needed when YouTube asks to "confirm you're not a bot") and downloads can be slowed down.
        foreach (var runtime in new[] { "deno", "node", "bun" }.Where(r => Find(r + ".exe") != null))
        {
            psi.ArgumentList.Add("--js-runtimes");
            psi.ArgumentList.Add(runtime);
        }
        return psi;
    }

    /// <summary>
    /// Starts yt-dlp or FFmpeg with Windows error message boxes turned off (child processes inherit this), so a
    /// library that Windows refuses to load cannot leave a "Bad Image" box waiting for someone to click OK.
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

    /// <summary>Looks next to OSDM, in the tools folder, then on PATH.</summary>
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

    /// <summary>Downloads whichever of Python, yt-dlp and FFmpeg is missing into %LOCALAPPDATA%\OSDM\tools.</summary>
    public static async Task InstallAsync(IProgress<string> status, CancellationToken ct)
    {
        Directory.CreateDirectory(Folder);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OSDM/0.4");

        if (!File.Exists(PythonExe))
        {
            string zip = Path.Combine(Folder, "python.zip");
            await FetchAsync(http, PythonZipUrl, zip, "Python", status, ct);
            status.Report("Unpacking Python...");
            string dir = Path.GetDirectoryName(PythonExe)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            ZipFile.ExtractToDirectory(zip, dir);
            File.Delete(zip);
        }
        if (!File.Exists(YtDlpPyz))
            await FetchAsync(http, YtDlpUrl, YtDlpPyz, "yt-dlp", status, ct);
        try
        {
            if (YtDlp != null) File.Delete(Path.Combine(Folder, "yt-dlp.exe")); // the old, unsigned version
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // still running; unused from now on

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
