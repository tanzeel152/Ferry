// Ferry Integration: hands downloads and detected videos to the Ferry app at http://localhost:<port>.

const DEFAULTS = {
  port: 51515,
  enabled: true,      // take over browser downloads
  videoButton: true,  // "Download video" button over videos (content.js)
  extensions: "zip rar 7z gz tar bz2 xz iso exe msi apk dmg bin mp4 mkv avi mov webm flv wmv m4v mp3 flac wav m4a aac ogg pdf doc docx xls xlsx ppt pptx epub torrent",
};
const MIME_EXT = {
  "application/zip": "zip", "application/x-zip-compressed": "zip", "application/pdf": "pdf",
  "application/x-msdownload": "exe", "application/x-msi": "msi", "application/vnd.rar": "rar",
  "application/x-rar-compressed": "rar", "application/x-7z-compressed": "7z", "application/x-iso9660-image": "iso",
};
const MAX_MEDIA_PER_TAB = 40;

const getSettings = () => chrome.storage.local.get(DEFAULTS);
const appUrl = (port, path) => `http://localhost:${port}${path}`;

/** Resolves to Ferry's version if it is running, otherwise null. */
async function appVersion(port) {
  try {
    const r = await fetch(appUrl(port, "/ping"), { cache: "no-store" });
    return r.ok ? (await r.json()).version || "" : null;
  } catch {
    return null;
  }
}

const appRunning = async (port) => (await appVersion(port)) !== null;

/** Brings the Ferry window to the front. */
async function openApp() {
  const { port } = await getSettings();
  try {
    const r = await fetch(appUrl(port, "/show"), { method: "POST" });
    return r.ok;
  } catch {
    return false;
  }
}

async function cookieHeader(url) {
  try {
    const list = await chrome.cookies.getAll({ url });
    return list.map((c) => `${c.name}=${c.value}`).join("; ") || null;
  } catch {
    return null;
  }
}

/** Sends {url, kind, referrer, fileName, pageUrl} to Ferry. Resolves to true if Ferry accepted it. */
async function sendToApp(data) {
  const { port } = await getSettings();
  const body = { userAgent: navigator.userAgent, cookies: await cookieHeader(data.url), ...data };
  try {
    const r = await fetch(appUrl(port, "/add"), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    return r.ok && (await r.json()).ok === true;
  } catch {
    return false;
  }
}

/**
 * Asks Ferry which qualities a video page offers (Ferry reads them with yt-dlp, which takes a few seconds).
 * Resolves to {ok, title, qualities: [{label, detail, format, size}]}, {ok: false, error} or {ok: false, offline: true}.
 */
async function videoFormats(data) {
  const { port } = await getSettings();
  const body = { userAgent: navigator.userAgent, cookies: await cookieHeader(data.url), ...data };
  try {
    const r = await fetch(appUrl(port, "/formats"), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    if (r.status === 404) return { ok: false, error: "This version of Ferry cannot list qualities. Update Ferry." };
    if (!r.ok) return { ok: false, error: `Ferry answered with error ${r.status}.` };
    return await r.json();
  } catch {
    return { ok: false, offline: true };
  }
}

function extensionOf(nameOrUrl) {
  const m = /\.([a-z0-9]{1,6})$/i.exec((nameOrUrl || "").split(/[?#]/)[0]);
  return m ? m[1].toLowerCase() : "";
}

// 1. Take over browser downloads of the chosen file types.
chrome.downloads.onCreated.addListener(async (item) => {
  const settings = await getSettings();
  const url = item.finalUrl || item.url;
  if (!settings.enabled || !/^https?:/i.test(url)) return; // blob:, data: and file: stay in the browser

  const ext = extensionOf(item.filename) || extensionOf(new URL(url).pathname) || MIME_EXT[item.mime] || "";
  const isMedia = /^(video|audio)\//i.test(item.mime || "");
  if (!settings.extensions.split(/\s+/).includes(ext) && !isMedia) return;

  try { await chrome.downloads.pause(item.id); } catch { return; } // already finished
  if (!(await appRunning(settings.port))) {
    chrome.downloads.resume(item.id).catch(() => {});
    return;
  }
  const accepted = await sendToApp({
    url,
    kind: "file",
    fileName: item.filename ? item.filename.split(/[\\/]/).pop() : null,
    referrer: item.referrer || null,
  });
  if (accepted) {
    await chrome.downloads.cancel(item.id).catch(() => {});
    await chrome.downloads.erase({ id: item.id }).catch(() => {});
  } else {
    chrome.downloads.resume(item.id).catch(() => {});
  }
});

// 2. Right-click menus.
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({ id: "link", title: "Download with Ferry", contexts: ["link"] });
    chrome.contextMenus.create({ id: "media", title: "Download this media with Ferry", contexts: ["video", "audio"] });
    chrome.contextMenus.create({ id: "page", title: "Download video on this page with Ferry", contexts: ["page"] });
  });
});

chrome.contextMenus.onClicked.addListener(async (info) => {
  if (info.menuItemId === "link") {
    await sendToApp({ url: info.linkUrl, referrer: info.pageUrl, pageUrl: info.pageUrl, kind: null });
  } else if (info.menuItemId === "media" && /^https?:/i.test(info.srcUrl || "")) {
    await sendToApp({ url: info.srcUrl, referrer: info.pageUrl, pageUrl: info.pageUrl, kind: "file" });
  } else {
    // Page menu, or a <video> playing from blob: (streamed): let yt-dlp work it out from the page.
    await sendToApp({ url: info.pageUrl, kind: "video" });
  }
});

// 3. Detect videos (HLS/DASH manifests and direct media files) and list them in the popup.
const tabKey = (tabId) => `tab_${tabId}`;

async function recordMedia(tabId, entry) {
  const key = tabKey(tabId);
  const stored = (await chrome.storage.session.get(key))[key] || [];
  if (stored.some((m) => m.url === entry.url)) return;
  stored.push(entry);
  if (stored.length > MAX_MEDIA_PER_TAB) stored.shift();
  await chrome.storage.session.set({ [key]: stored });
  chrome.action.setBadgeBackgroundColor({ tabId, color: "#2563eb" });
  chrome.action.setBadgeText({ tabId, text: String(stored.length) });
}

async function clearMedia(tabId) {
  await chrome.storage.session.remove(tabKey(tabId));
  chrome.action.setBadgeText({ tabId, text: "" }).catch(() => {});
}

chrome.webRequest.onHeadersReceived.addListener(
  (d) => {
    if (d.tabId < 0) return;
    const header = (name) => (d.responseHeaders || []).find((h) => h.name.toLowerCase() === name)?.value || "";
    const type = header("content-type").split(";")[0].trim().toLowerCase();
    const path = new URL(d.url).pathname.toLowerCase();

    const manifest = /\.(m3u8|mpd)$/.test(path) || /mpegurl|dash\+xml/.test(type);
    const segment = /\.(ts|m4s|aac|vtt)$/.test(path) || /^bytes [1-9]/.test(header("content-range"));
    const media = /^(video|audio)\//.test(type) && !segment;
    if (!manifest && !media) return;

    recordMedia(d.tabId, {
      url: d.url,
      kind: manifest ? "video" : "file",
      label: manifest ? (path.endsWith(".mpd") || type.includes("dash") ? "DASH stream" : "HLS stream") : type,
      size: parseInt(header("content-length"), 10) || 0,
      time: Date.now(),
    });
  },
  { urls: ["<all_urls>"], types: ["media", "xmlhttprequest", "other", "object"] },
  ["responseHeaders"]
);

chrome.tabs.onUpdated.addListener((tabId, change) => {
  if (change.status === "loading" && change.url) clearMedia(tabId);
});
chrome.tabs.onRemoved.addListener((tabId) => clearMedia(tabId));

// Messages from the popup and from the video button (content.js).
chrome.runtime.onMessage.addListener((msg, _sender, reply) => {
  if (msg.type === "send") sendToApp(msg.data).then(reply);
  else if (msg.type === "formats") videoFormats(msg.data).then(reply);
  else if (msg.type === "open") openApp().then(reply);
  else if (msg.type === "status") getSettings().then((s) => appVersion(s.port)).then((version) => reply({ running: version !== null, version }));
  else return false;
  return true; // reply asynchronously
});
