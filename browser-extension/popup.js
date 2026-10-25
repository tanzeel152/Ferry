const $ = (id) => document.getElementById(id);
const message = (msg) => chrome.runtime.sendMessage(msg);

// Line icons, the same shapes as in the app (OSDM\Icons.cs).
const ICONS = {
  video: '<rect x="2.5" y="5" width="19" height="14" rx="3"/><path d="M10 9v6l5.5-3z" fill="currentColor" stroke="none"/>',
  stream: '<path d="M12 3 2.5 8 12 13l9.5-5z"/><path d="m2.5 12.5 9.5 5 9.5-5"/><path d="m2.5 16.5 9.5 5 9.5-5"/>',
  audio: '<circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/><path d="M9 18V5l12-2v13"/>',
  download: '<path d="M12 3.5V15M7 10l5 5 5-5M3.5 15v5h17v-5"/>',
  check: '<path d="m5 12.5 4.5 4.5L19 7.5"/>',
  x: '<path d="M6 6l12 12M18 6 6 18"/>',
  open: '<path d="M14 3h7v7M10 14 21 3M18 14v6H4V6h6"/>',
};

function svg(name) {
  const template = document.createElement("template");
  template.innerHTML = `<svg viewBox="0 0 24 24">${ICONS[name]}</svg>`;
  return template.content.firstChild;
}

/** Sets a button to an icon plus text (text is set as text, never as HTML). */
function setLabel(button, icon, text) {
  const span = document.createElement("span");
  span.textContent = text ?? "";
  button.replaceChildren(svg(icon), ...(text ? [span] : []));
}

/** Shows success or failure on a button for a moment, then puts it back. */
function flash(button, ok, text, restore) {
  button.classList.add(ok ? "ok" : "bad");
  setLabel(button, ok ? "check" : "x", text);
  setTimeout(() => {
    button.classList.remove("ok", "bad");
    restore();
  }, 1800);
}

function size(bytes) {
  if (!bytes) return "";
  const units = ["B", "KB", "MB", "GB"];
  let i = 0;
  while (bytes >= 1024 && i < units.length - 1) { bytes /= 1024; i++; }
  return `${bytes.toFixed(i ? 1 : 0)} ${units[i]}`;
}

function nameOf(url) {
  try {
    const last = new URL(url).pathname.split("/").filter(Boolean).pop();
    return decodeURIComponent(last || url);
  } catch {
    return url;
  }
}

const kindOf = (m) => (/stream/i.test(m.label) ? "stream" : /^audio\//i.test(m.label) ? "audio" : "video");

/** "HLS stream", or "MP4 video" for a content type like video/mp4. */
function describe(m) {
  if (kindOf(m) === "stream") return m.label;
  const [type, sub = ""] = m.label.split("/");
  return `${sub.replace(/^x-/, "").toUpperCase()} ${type}`.trim();
}

async function checkStatus() {
  const status = await message({ type: "status" }).catch(() => ({ running: false }));
  $("status").className = `pill ${status.running ? "ok" : "bad"}`;
  $("status").querySelector("b").textContent = status.running ? "Connected" : "Not running";
  $("offline").hidden = status.running;
  $("version").textContent = status.running && status.version ? `OSDM ${status.version}` : "";
  return status.running;
}

function showMedia(media, tab) {
  const list = $("media");
  $("count").textContent = media.length ? String(media.length) : "";
  if (media.length === 0) {
    const li = document.createElement("li");
    li.className = "empty";
    li.textContent = "Nothing yet. Play a video, then open this again.";
    list.replaceChildren(li);
    return;
  }
  list.replaceChildren(...media.slice().reverse().map((m) => {
    const li = document.createElement("li");
    const kind = document.createElement("span");
    kind.className = `kind ${kindOf(m)}`;
    kind.append(svg(kindOf(m) === "stream" ? "stream" : kindOf(m)));

    const info = document.createElement("div");
    info.className = "info";
    const name = document.createElement("div");
    name.className = "name";
    name.textContent = nameOf(m.url);
    name.title = m.url;
    const meta = document.createElement("div");
    meta.className = "meta";
    meta.textContent = [describe(m), size(m.size)].filter(Boolean).join("  ·  ");
    info.append(name, meta);

    const get = document.createElement("button");
    get.className = "get";
    get.title = "Download with OSDM";
    get.append(svg("download"));
    get.onclick = async () => {
      const ok = await message({ type: "send", data: { url: m.url, kind: m.kind, referrer: tab.url, pageUrl: tab.url } });
      flash(get, ok, "", () => setLabel(get, "download"));
      if (!ok) checkStatus();
    };

    li.append(kind, info, get);
    return li;
  }));
}

async function main() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  checkStatus();

  // Download the video on this page (yt-dlp works it out from the page address).
  const page = $("page");
  const pageLabel = () => setLabel(page, "video", "Download video on this page");
  const webPage = /^https?:/i.test(tab?.url || "");
  if (webPage) pageLabel();
  else setLabel(page, "video", "Not available on this page");
  page.disabled = !webPage;
  page.onclick = async () => {
    const ok = await message({ type: "send", data: { url: tab.url, kind: "video", pageUrl: tab.url } });
    flash(page, ok, ok ? "Sent to OSDM" : "Could not reach OSDM", pageLabel);
    if (!ok) checkStatus();
  };

  // Media the extension saw this tab load (HLS/DASH streams, video and audio files).
  const key = `tab_${tab?.id}`;
  showMedia((await chrome.storage.session.get(key))[key] || [], tab);

  // Settings
  const settings = await chrome.storage.local.get({ enabled: true, videoButton: true, port: 51515 });
  $("enabled").checked = settings.enabled;
  $("videoButton").checked = settings.videoButton;
  $("port").value = settings.port;
  $("enabled").onchange = () => chrome.storage.local.set({ enabled: $("enabled").checked });
  $("videoButton").onchange = () => chrome.storage.local.set({ videoButton: $("videoButton").checked });
  $("port").onchange = async () => {
    const port = parseInt($("port").value, 10);
    if (port >= 1024 && port <= 65535) {
      await chrome.storage.local.set({ port });
      checkStatus();
    }
  };

  $("retry").onclick = checkStatus;
  const open = $("open");
  setLabel(open, "open", "Open OSDM");
  open.onclick = async () => {
    if (await message({ type: "open" })) window.close();
    else checkStatus();
  };
}

main();
