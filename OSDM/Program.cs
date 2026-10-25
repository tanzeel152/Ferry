using System.Runtime.InteropServices;
using OSDM.Core;

namespace OSDM;

static class Program
{
    const string MutexName = @"Local\OSDM.SingleInstance";
    // A second OSDM.exe uses these to bring up the running copy's window, or to make it exit (the installer does that).
    const string ShowEventName = @"Local\OSDM.Show";
    const string QuitEventName = @"Local\OSDM.Quit";

    [STAThread]
    static void Main(string[] args)
    {
        if (HasFlag(args, "--quit"))
        {
            QuitRunningCopy();
            return;
        }
        // "Start with Windows" passes --tray: run in the tray without opening the window.
        bool tray = HasFlag(args, "--tray");
        string? url = args.FirstOrDefault(a =>
            Uri.TryCreate(a, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps));

        using var mutex = new Mutex(true, MutexName, out bool firstCopy);
        if (!firstCopy)
        {
            // OSDM is already running: hand the URL over (or bring its window up) and exit.
            if (url != null)
                BrowserBridge.SendToRunningAsync(Storage.LoadSettings().BridgePort, new AddRequest { Url = url }).GetAwaiter().GetResult();
            else if (!tray)
            {
                AllowSetForegroundWindow(ASFW_ANY);
                Signal(ShowEventName);
            }
            return;
        }

        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);

        ApplicationConfiguration.Initialize();
        ToolStripManager.Renderer = new FlatRenderer();
        var form = new MainForm(url, startHidden: tray);
        ThreadPool.RegisterWaitForSingleObject(show, (_, _) => form.BeginInvokeSafe(form.ShowFromTray), null, Timeout.Infinite, executeOnlyOnce: false);
        ThreadPool.RegisterWaitForSingleObject(quit, (_, _) => form.BeginInvokeSafe(form.ExitApp), null, Timeout.Infinite, executeOnlyOnce: true);
        Application.Run(form);
    }

    /// <summary>"OSDM.exe --quit": asks the running copy to save and exit, and waits (up to 20 s) until its process is gone.</summary>
    static void QuitRunningCopy()
    {
        if (!Signal(QuitEventName)) return;
        using var mutex = new Mutex(false, MutexName);
        try
        {
            if (mutex.WaitOne(TimeSpan.FromSeconds(20))) mutex.ReleaseMutex();
        }
        catch (AbandonedMutexException)
        {
            // The running copy exited while owning the mutex; we own it now.
            mutex.ReleaseMutex();
        }
    }

    static bool Signal(string eventName)
    {
        if (!EventWaitHandle.TryOpenExisting(eventName, out var handle)) return false;
        using (handle) handle.Set();
        return true;
    }

    static bool HasFlag(string[] args, string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    const int ASFW_ANY = -1;

    // Lets the running copy bring its window to the front when this (foreground) process asks it to.
    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
}
