# Changelog

## 0.5.0 - 2026-10-26

**OSDM is now Ferry, and it is open source** under the GNU GPL v3.

- New name: Ferry. Setup replaces an installed OSDM, and Ferry moves over OSDM's settings, download list, unfinished downloads and video tools when it first starts. The browser extension has to be loaded again from `C:\Program Files\Ferry\browser-extension` (remove the old "OSDM Integration" first).
- Help menu: **Report a problem**, **Check for updates** and **Source code on GitHub**.
- Releases are built by GitHub Actions and come with an installer, a portable zip, the extension on its own, and SHA-256 checksums.

## 0.4.0 - 2026-10-26

- yt-dlp runs from the official, signed embeddable Python (`yt-dlp.pyz`) instead of `yt-dlp.exe`, so Windows 11 Smart App Control no longer blocks it. Installs from 0.3 are switched over automatically.
- Video downloads use several connections: stream pieces download in parallel, and YouTube videos are fetched in 10 MB pieces.
- If Node.js, Deno or Bun is installed, yt-dlp uses it for YouTube, which makes signed-in downloads work and avoids slowed-down downloads.

## 0.3.0 - 2026-10-25

First version, as OSDM: multi-connection downloads with pause and resume, Chrome/Edge extension with a "Download video" panel, video downloads with yt-dlp and FFmpeg, light and dark themes, and an installer.
