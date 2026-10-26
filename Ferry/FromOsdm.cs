using Microsoft.Win32;
using Ferry.Core;

namespace Ferry;

/// <summary>
/// OSDM is Ferry's old name. On Ferry's first start, moves OSDM's settings, download list, video tools, partial files
/// and "Start with Windows" entry over. The installer already moves the video tools and uninstalls OSDM; this also
/// covers copies of OSDM that were run without installing.
/// </summary>
static class FromOsdm
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Migrate()
    {
        string oldData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OSDM");
        string oldTools = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OSDM", "tools");
        bool moveData = Directory.Exists(oldData) && !Directory.Exists(Storage.DataFolder);
        bool moveTools = Directory.Exists(oldTools) && !Directory.Exists(Tools.Folder);

        // A running OSDM holds the extension port and writes to its folders, so close it first.
        if (moveData || moveTools) Program.QuitRunningCopy(@"Local\OSDM.Quit", @"Local\OSDM.SingleInstance");

        if (moveTools)
            Try(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Tools.Folder)!);
                Directory.Move(oldTools, Tools.Folder);
                Directory.Delete(Path.GetDirectoryName(oldTools)!); // only if empty
            });

        if (moveData)
            Try(() =>
            {
                Directory.Move(oldData, Storage.DataFolder);
                foreach (var item in Storage.LoadDownloads())
                {
                    string oldPart = item.FullPath + ".osdm";
                    if (item.Status != DownloadStatus.Completed && File.Exists(oldPart) && !File.Exists(item.PartPath))
                        Try(() => File.Move(oldPart, item.PartPath));
                }
            });

        Try(() =>
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue("OSDM") is not string) return;
            run.DeleteValue("OSDM", throwOnMissingValue: false);
            StartWithWindows.Enabled = true;
        });
    }

    /// <summary>Each step is skipped if it fails (a file in use, say); Ferry then starts fresh for that part.</summary>
    static void Try(Action step)
    {
        try { step(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
