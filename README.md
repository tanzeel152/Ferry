# OSDM: Open Source Download Manager

A Windows download manager written in C# / .NET 8.

## Features

- **Multi-connection downloads.** Each file is split into up to 32 byte ranges that download in parallel. When a connection finishes early, it takes over half of the largest remaining range, so every connection stays busy.
- **Pause and resume**, including after closing OSDM or restarting Windows. Uses HTTP `Range` and `If-Range`, so a file that changed on the server is not stitched together wrongly.
- **Download window** with a sidebar of categories (with counts), a big-icon toolbar, search, a download list with file icons, progress bars and colored status, and a details panel that shows how far each connection has got. A tray icon and completion notifications.
- **Light and dark themes**, following Windows by default (Settings > General > Appearance). All icons are OSDM's own, drawn in code.
- **Browser extension** (Chrome and Edge) that:
  - takes over downloads of the file types you choose
  - adds right-click menus ("Download with OSDM", "Download video on this page")
  - shows a **Download video ▾** panel in the corner of any large video you point at. It opens a menu of that video's qualities with their sizes (4320p … 144p, Audio only), read by OSDM with yt-dlp in a few seconds. The **✕** hides the panel for that video; the popup can turn it off everywhere.
  - detects HLS/DASH streams and media files on the page and lists them in its popup, which also shows whether OSDM is running and can bring its window up
- **Video downloads** from YouTube and about 1,800 other sites, plus HLS (`.m3u8`) and DASH (`.mpd`) streams, using yt-dlp and FFmpeg. They use several connections too: stream pieces download in parallel, and YouTube videos are fetched in 10 MB pieces. The quality picked in the browser shows in OSDM's download window, where it can be changed (or set to one of the Settings presets).
- Speed limit, proxy (HTTP or SOCKS), retries, automatic file naming (`Content-Disposition`, de-duplicated names), clipboard monitoring, drag and drop, single instance (`OSDM.exe <url>` hands the URL to the running copy).

## Install

Run `dist\OSDM-Setup-0.4.0.exe`. It installs OSDM to `C:\Program Files\OSDM` and adds:

- Start menu and desktop shortcuts
- an entry in Control Panel > Programs and Features (and Settings > Apps) with an uninstaller
- optionally, **Start OSDM when Windows starts**: OSDM then waits in the taskbar tray to catch downloads. You can change this later under **Tools > Start OSDM when Windows starts**.

Setup and the uninstaller close a running OSDM first. Uninstalling keeps your settings, the download list and downloaded files, and removes the video tools.

To pin OSDM to the taskbar, right-click it in the Start menu and choose **Pin to taskbar** (Windows does not let programs pin themselves). If the tray icon is hidden behind the `^` arrow, turn OSDM on under Settings > Personalization > Taskbar > Other system tray icons.

## Build

Needs the .NET 8 SDK, and Inno Setup 6 for the installer (`winget install JRSoftware.InnoSetup`).

```powershell
cd OSDM                                            # the repository folder
dotnet build                                       # debug build
dotnet publish OSDM\OSDM.csproj -c Release -o publish   # one self-contained OSDM.exe
.\build-installer.ps1                              # publish, then dist\OSDM-Setup-<version>.exe
```

`publish\OSDM.exe` runs on any 64-bit Windows 10/11 PC without installing .NET. The `browser-extension` folder is copied next to it.

Command line: `OSDM.exe <url>` adds a download, `--tray` starts without opening the window, `--quit` makes the running copy save and exit.

## Set up the browser extension

1. Start OSDM.
2. Open `chrome://extensions` (or `edge://extensions`) and turn on **Developer mode**.
3. Click **Load unpacked** and choose `C:\Program Files\OSDM\browser-extension` (or `publish\browser-extension` if you did not install).

The extension talks to OSDM at `http://localhost:51515`. OSDM only accepts requests from browser extensions or local programs, so websites cannot add downloads. To change the port, set it in OSDM's settings and in the extension's popup.

## Video downloads

Use **Tools > Install video tools** once. It downloads:

- [yt-dlp](https://github.com/yt-dlp/yt-dlp) (Unlicense), which finds the video streams on a page. OSDM installs its plain-Python version (`yt-dlp.pyz`, 3 MB)...
- ...and the official [embeddable Python](https://www.python.org/downloads/windows/) 3.13 (PSF license, 11 MB) to run it.
- [FFmpeg](https://ffmpeg.org) (GPL build from BtbN), which merges video and audio

They go into `%LOCALAPPDATA%\OSDM\tools`. OSDM also uses an `ffmpeg.exe` placed next to `OSDM.exe` or on `PATH`. Installs from OSDM 0.3 (which used `yt-dlp.exe`) are switched over automatically when OSDM starts.

If [Node.js](https://nodejs.org), [Deno](https://deno.com) or [Bun](https://bun.sh) is installed, yt-dlp uses it to run YouTube's player scripts. That makes signed-in downloads work (needed when YouTube asks to "confirm you're not a bot") and avoids slowed-down YouTube downloads.

Notes:

- **Why not `yt-dlp.exe`?** Windows 11's **Smart App Control** blocks unsigned programs it does not know yet, and shows a "Smart App Control blocked part of this app" notice. `yt-dlp.exe` is unsigned and unpacks unsigned libraries every time it runs (one of them, `Cryptodome\Hash\_SHA1.pyd`, was blocked on every run), and each yt-dlp update starts over. All 30 program files of the embeddable Python are signed by the Python Software Foundation (and Microsoft), so Smart App Control allows them, and `yt-dlp.pyz` is Python code, not a program file.
- **Browser cookies are not sent at first.** With YouTube sign-in cookies and no JavaScript runtime, yt-dlp finds no formats ("Requested format is not available"). OSDM retries with the browser's cookies only when the site asks you to sign in (private, age-restricted or members-only videos, or YouTube's "not a bot" check).
- **Smart App Control can still block `ffmpeg.exe`** (unsigned) for a while after it is first installed ("An Application Control policy has blocked this file"). OSDM then stops with that message instead of leaving the video and sound as two files. Press **Resume** to try again; parts already downloaded are kept.
- **Speed limit:** yt-dlp applies a speed limit to each connection, so a video download uses one connection while a limit is set.
- A YouTube or other video-site link added as a **File** is switched to a video download instead of being saved as `watch.html`.

## Where data is kept

| What | Where |
|---|---|
| Settings and download list | `%APPDATA%\OSDM\settings.json`, `downloads.json` |
| Downloads | `Downloads\OSDM\<category>\` (changeable in Settings) |
| Partial files | `<file name>.osdm`, next to the final file |

## Project layout

| Path | What it does |
|---|---|
| `OSDM.Core\HttpEngine.cs` | Multi-connection engine: probing, range splitting, workers, retries, writing to disk |
| `OSDM.Core\Net.cs` | HTTP client, request headers, redirects (cookies are not sent to other sites) |
| `OSDM.Core\VideoEngine.cs` | Runs yt-dlp and reads its progress |
| `OSDM.Core\DownloadManager.cs` | Queue, start/pause/remove, speed sampling, saving |
| `OSDM.Core\BrowserBridge.cs` | Local HTTP server for the extension |
| `OSDM.Core\Tools.cs` | Finds or installs Python, yt-dlp and FFmpeg; starts yt-dlp |
| `OSDM\MainForm*.cs` | Main window, menus, tray icon, clipboard monitoring |
| `OSDM\Sidebar.cs`, `DownloadListView.cs`, `DetailsPanel.cs`, `EmptyState.cs` | The custom-drawn parts of the main window |
| `OSDM\Theme.cs`, `FlatRenderer.cs`, `Icons.cs`, `Shapes.cs` | Light/dark colors, flat menus and toolbar, line icons |
| `OSDM\AddUrlForm.cs`, `SettingsForm.cs` | Add-download and settings windows |
| `OSDM\Program.cs` | Single instance, `--tray`, `--quit`, bringing up the running copy |
| `OSDM\StartWithWindows.cs` | The "Start with Windows" registry entry |
| `browser-extension\` | Chrome/Edge extension (Manifest V3) |
| `installer\OSDM.iss`, `build-installer.ps1` | Inno Setup installer and the script that builds it |

## Not built yet

- Scheduler and named queues (start/stop at set times)
- Site grabber (download every file linked from a site)
- Firefox version of the extension
- Explorer right-click menu, auto-update
- Per-download speed limits, checksum verification

Network drivers like IDM's "Advanced Integration" are not planned. They need a signed kernel driver, and the browser extension covers modern HTTPS sites anyway.
