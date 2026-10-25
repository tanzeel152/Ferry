using Microsoft.Win32;

namespace OSDM;

/// <summary>
/// "Start OSDM when Windows starts": a value under HKCU\...\Run that launches OSDM in the tray.
/// The installer's "Start with Windows" option writes the same value, and the uninstaller removes it.
/// </summary>
static class StartWithWindows
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "OSDM";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
