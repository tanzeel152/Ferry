// OSDM Integration: a "Download video" panel over videos on web pages, with a menu of the video's qualities.
(() => {
  if (window.osdmVideoButton) return; // already running in this frame
  window.osdmVideoButton = true;

  const MIN_WIDTH = 240;
  const MIN_HEIGHT = 135;
  const MENU_WIDTH = 290;
  const LABEL = "Download video";
  // On these sites the page URL is what yt-dlp needs, not the <video> source (which is usually a blob: stream).
  const VIDEO_HOSTS = [
    "youtube.com", "youtu.be", "vimeo.com", "facebook.com", "fb.watch", "instagram.com", "tiktok.com",
    "twitter.com", "x.com", "dailymotion.com", "twitch.tv", "reddit.com", "soundcloud.com", "bilibili.com", "ok.ru", "vk.com",
  ];
  const ICONS = {
    download: "M12 4v10.5M7.5 10l4.5 4.5 4.5-4.5M5 19.5h14",
    chevron: "M7 10l5 5 5-5",
    close: "M7 7l10 10M17 7L7 17",
    video: "M4 7.5A1.5 1.5 0 0 1 5.5 6h9A1.5 1.5 0 0 1 16 7.5v9a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 4 16.5zM16 10.5l4-2.5v8l-4-2.5",
    music: "M9 17.5V6l10-2v11.5M9 17.5a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0zM19 15.5a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0z",
  };
  const CSS = `
    .wrap { position: relative; font: 13px/1.25 "Segoe UI", system-ui, sans-serif; letter-spacing: 0; color: #fff; }
    button { all: unset; box-sizing: border-box; display: flex; align-items: center; cursor: pointer; color: inherit; }
    svg { width: 14px; height: 14px; flex: none; fill: none; stroke: #fff; stroke-width: 2.4; stroke-linecap: round; stroke-linejoin: round; }

    .bar {
      display: flex; align-items: center; height: 34px; border-radius: 17px; overflow: hidden;
      background: rgba(17, 24, 39, 0.88); box-shadow: 0 4px 16px rgba(0, 0, 0, 0.35); transition: background 0.15s;
    }
    .bar.ok { background: #16a34a; }
    .bar.bad { background: #dc2626; }
    .main { gap: 8px; height: 34px; padding: 0 10px 0 5px; font-weight: 600; white-space: nowrap; transition: background 0.15s; }
    .main:hover, .main[aria-expanded="true"] { background: #2563eb; }
    .main:active { background: #1d4ed8; }
    .badge { display: grid; place-items: center; width: 24px; height: 24px; border-radius: 50%; background: #2563eb; flex: none; }
    .main:hover .badge, .main[aria-expanded="true"] .badge, .ok .badge, .bad .badge { background: rgba(255, 255, 255, 0.22); }
    .chev { width: 12px; height: 12px; opacity: 0.85; transition: transform 0.15s; }
    .main[aria-expanded="true"] .chev { transform: rotate(180deg); }
    .sep { width: 1px; height: 18px; background: rgba(255, 255, 255, 0.2); flex: none; }
    .close { justify-content: center; width: 30px; height: 34px; opacity: 0.75; }
    .close:hover { opacity: 1; background: rgba(255, 255, 255, 0.14); }
    .close svg { width: 11px; height: 11px; }

    .menu {
      position: absolute; top: 40px; right: 0; width: ${MENU_WIDTH}px; max-height: 480px; overflow-y: auto; box-sizing: border-box;
      padding: 6px; border-radius: 12px; background: rgba(17, 24, 39, 0.97); border: 1px solid rgba(255, 255, 255, 0.08);
      box-shadow: 0 12px 32px rgba(0, 0, 0, 0.45); scrollbar-width: thin; scrollbar-color: rgba(255, 255, 255, 0.25) transparent;
    }
    .menu[hidden] { display: none; }
    .menu.up { top: auto; bottom: 40px; }
    .menu.left { right: auto; left: 0; }
    .head { padding: 7px 10px 9px; margin-bottom: 4px; border-bottom: 1px solid rgba(255, 255, 255, 0.08); }
    .title { font-weight: 600; font-size: 12.5px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .sub { margin-top: 2px; font-size: 11.5px; color: #9ca3af; }
    .item { width: 100%; gap: 10px; padding: 8px 10px; border-radius: 8px; }
    .item:hover, .item:focus-visible { background: #2563eb; }
    .item svg { stroke: #9ca3af; }
    .item:hover svg, .item:focus-visible svg { stroke: #fff; }
    .name { flex: 1; font-weight: 600; }
    .detail { font-size: 12px; color: #9ca3af; white-space: nowrap; }
    .item:hover .detail, .item:focus-visible .detail { color: #dbeafe; }
    .status { display: flex; align-items: center; gap: 10px; padding: 12px 10px; font-size: 12.5px; color: #d1d5db; }
    .status.error { align-items: flex-start; color: #fca5a5; }
    .spin {
      width: 16px; height: 16px; flex: none; box-sizing: border-box; border-radius: 50%;
      border: 2px solid rgba(255, 255, 255, 0.2); border-top-color: #60a5fa; animation: spin 0.8s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
  `;

  let enabled = true;
  chrome.storage.local.get({ videoButton: true }).then((s) => { enabled = s.videoButton; });
  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== "local" || !changes.videoButton) return;
    enabled = changes.videoButton.newValue;
    if (!enabled) hide(true);
  });

  // The panel lives in a closed shadow root: page CSS cannot restyle it, and its CSS cannot leak into the page.
  const host = document.createElement("osdm-video-button");
  host.style.cssText = "all: initial; position: fixed; z-index: 2147483647; top: 0; left: 0; display: none;";
  const root = host.attachShadow({ mode: "closed" });
  const sheet = new CSSStyleSheet();
  sheet.replaceSync(CSS);
  root.adoptedStyleSheets = [sheet];

  const wrap = element("div", { class: "wrap" });
  const bar = element("div", { class: "bar" });
  const main = element("button", { type: "button", class: "main", title: "Download this video with OSDM", "aria-haspopup": "menu", "aria-expanded": "false" });
  const badge = element("span", { class: "badge" });
  badge.append(icon("download"));
  const label = element("span", {}, LABEL);
  const chevron = icon("chevron", "chev");
  main.append(badge, label, chevron);
  const close = element("button", { type: "button", class: "close", title: "Hide for this video", "aria-label": "Hide for this video" });
  close.append(icon("close"));
  bar.append(main, element("span", { class: "sep" }), close);
  const menu = element("div", { class: "menu", role: "menu", hidden: "" });
  wrap.append(bar, menu);
  root.append(wrap);

  let current = null;  // the video the panel is showing for
  let target = null;   // what to send to OSDM for it (see request)
  let hideTimer = 0;
  let busy = false;    // sending
  let menuOpen = false;
  const dismissed = new Set(); // links whose panel was closed with the X
  const qualityLists = new Map(); // link -> promise of OSDM's answer

  function element(tag, attrs, text) {
    const e = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v);
    if (text) e.textContent = text;
    return e;
  }

  function icon(name, cls) {
    const ns = "http://www.w3.org/2000/svg";
    const svg = document.createElementNS(ns, "svg");
    svg.setAttribute("viewBox", "0 0 24 24");
    if (cls) svg.setAttribute("class", cls);
    const p = document.createElementNS(ns, "path");
    p.setAttribute("d", ICONS[name]);
    svg.append(p);
    return svg;
  }

  const big = (r) => r.width >= MIN_WIDTH && r.height >= MIN_HEIGHT;

  function videoAt(x, y) {
    for (const v of document.querySelectorAll("video")) {
      const r = v.getBoundingClientRect();
      if (big(r) && x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) return v;
    }
    return null;
  }

  /** What to send to OSDM for this video, or null if there is nothing sensible to send. */
  function request(video) {
    const page = location.href;
    const hostname = location.hostname.replace(/^www\./, "");
    const onVideoSite = VIDEO_HOSTS.some((h) => hostname === h || hostname.endsWith("." + h));
    const src = video.currentSrc || video.src || "";
    if (!onVideoSite && /^https?:/i.test(src)) return { url: src, kind: "file", referrer: page, pageUrl: page };

    // Preview players inside a link (video cards) belong to the linked page.
    const link = video.closest("a[href]");
    const url = link && /^https?:/i.test(link.href) ? link.href : page;
    // YouTube's home and search pages play previews; only watch pages are downloadable as-is.
    if (url === page && hostname.endsWith("youtube.com") && !/^\/(watch|shorts|embed|live)/.test(location.pathname)) return null;
    return { url, kind: "video", referrer: page, pageUrl: page };
  }

  // In full screen only the full-screen element is visible, so the panel moves into it.
  const container = () => {
    const fs = document.fullscreenElement;
    return fs && !(fs instanceof HTMLVideoElement) ? fs : document.documentElement;
  };

  function show(video) {
    if (!enabled || busy || menuOpen) return;
    const data = request(video);
    if (!data || dismissed.has(data.url)) return;
    const parent = container();
    if (host.parentNode !== parent) parent.append(host);
    current = video;
    target = data;
    chevron.style.display = data.kind === "video" ? "" : "none"; // files have no quality choice
    host.style.display = "block";
    place();
    clearTimeout(hideTimer);
    hideTimer = setTimeout(hide, 2500);
  }

  function hide(force) {
    if ((busy || menuOpen) && !force) return;
    clearTimeout(hideTimer);
    closeMenu();
    host.style.display = "none";
    current = null;
  }

  function place() {
    if (!current || !current.isConnected) return hide(true);
    const r = current.getBoundingClientRect();
    if (!big(r) || r.bottom < 0 || r.top > innerHeight || r.right < 0 || r.left > innerWidth) return hide(true);
    const top = Math.min(Math.max(r.top, 0) + 12, innerHeight - 46);
    const right = Math.min(r.right, innerWidth) - 12;
    const left = Math.max(8, right - host.offsetWidth);
    host.style.top = `${top}px`;
    host.style.left = `${left}px`;
    if (menuOpen) {
      // Below the panel unless there is more room above it, as tall as the room allows (it scrolls past that);
      // right-aligned unless that would run off the left edge.
      const below = innerHeight - top - 48;
      const above = top - 8;
      const up = below < Math.min(menu.scrollHeight, 300) && above > below;
      menu.classList.toggle("up", up);
      menu.style.maxHeight = `${Math.max(120, Math.min(480, up ? above : below))}px`;
      menu.classList.toggle("left", left + host.offsetWidth - MENU_WIDTH < 8);
    }
  }

  function setState(state, text) {
    bar.className = `bar ${state}`;
    label.textContent = text;
  }

  async function send(data) {
    if (busy) return;
    busy = true;
    closeMenu();
    clearTimeout(hideTimer);
    setState("", "Sending...");
    let ok = false;
    try {
      ok = await chrome.runtime.sendMessage({ type: "send", data });
    } catch {
      ok = false; // extension was reloaded or removed; the page needs a refresh
    }
    setState(ok ? "ok" : "bad", ok ? "Sent to OSDM" : "OSDM is not running");
    place();
    setTimeout(() => {
      busy = false;
      setState("", LABEL);
      hide();
    }, 2200);
  }

  /** OSDM's list of qualities for a link. Asked once per link; asked again only after a failure. */
  function qualitiesFor(data) {
    let answer = qualityLists.get(data.url);
    if (!answer) {
      answer = chrome.runtime
        .sendMessage({ type: "formats", data: { url: data.url, referrer: data.referrer, pageUrl: data.pageUrl } })
        .catch(() => ({ ok: false, error: "The OSDM extension was updated. Reload this page and try again." }));
      qualityLists.set(data.url, answer);
      answer.then((r) => { if (!r || !r.ok) qualityLists.delete(data.url); });
    }
    return answer;
  }

  function openMenu() {
    const data = target;
    menuOpen = true;
    clearTimeout(hideTimer);
    main.setAttribute("aria-expanded", "true");
    menu.hidden = false;
    renderStatus("spin", "Finding qualities...");
    place();
    qualitiesFor(data).then((answer) => {
      if (menuOpen && target === data) renderQualities(answer || {}, data);
    });
  }

  function closeMenu() {
    if (!menuOpen) return;
    menuOpen = false;
    main.setAttribute("aria-expanded", "false");
    menu.hidden = true;
  }

  function renderStatus(kind, text) {
    const row = element("div", { class: kind === "error" ? "status error" : "status" });
    if (kind === "spin") row.append(element("span", { class: "spin" }));
    row.append(element("span", {}, text));
    menu.replaceChildren(row);
  }

  function menuItem(iconName, name, detail, onClick) {
    const item = element("button", { type: "button", class: "item", role: "menuitem" });
    item.append(icon(iconName), element("span", { class: "name" }, name));
    if (detail) item.append(element("span", { class: "detail" }, detail));
    item.addEventListener("click", (e) => {
      e.preventDefault();
      e.stopPropagation();
      onClick();
    });
    return item;
  }

  function renderQualities(answer, data) {
    if (answer.offline) {
      renderStatus("error", "OSDM is not running. Start OSDM, then try again.");
    } else if (!answer.ok || !Array.isArray(answer.qualities)) {
      const reason = String(answer.error || "OSDM could not read this video.");
      renderStatus("error", reason.length > 180 ? reason.slice(0, 177) + "..." : reason);
      menu.append(menuItem("download", "Download in the usual quality", "", () => send(data)));
    } else {
      const head = element("div", { class: "head" });
      head.append(element("div", { class: "title", title: answer.title || "" }, answer.title || "This video"), element("div", { class: "sub" }, "Choose a quality"));
      menu.replaceChildren(head);
      for (const q of answer.qualities) {
        const quality = q.detail ? `${q.label} (${q.detail})` : q.label;
        menu.append(menuItem(q.label === "Audio only" ? "music" : "video", q.label, q.detail, () => send({ ...data, format: q.format, quality })));
      }
    }
    place();
  }

  // Show when the pointer is over a large video. Players often cover the <video> with their own controls,
  // so this checks the pointer position rather than relying on events from the video element itself.
  document.addEventListener("pointermove", (e) => {
    if (!enabled) return;
    if (e.target === host) {
      clearTimeout(hideTimer); // over the panel: keep it
      return;
    }
    if (menuOpen) return; // keep the menu on the video it was opened for
    const video = videoAt(e.clientX, e.clientY);
    if (video) show(video);
  }, { capture: true, passive: true });

  // Also show briefly when a video starts playing (but not for short clips such as ads and GIF-like loops).
  document.addEventListener("play", (e) => {
    const v = e.target;
    if (v instanceof HTMLVideoElement && big(v.getBoundingClientRect()) && !(v.duration < 10)) show(v);
  }, true);

  // A click anywhere else, or Esc, closes the menu.
  document.addEventListener("pointerdown", (e) => {
    if (!menuOpen || e.composedPath().includes(host)) return;
    closeMenu();
    hideTimer = setTimeout(hide, 1200);
  }, true);
  document.addEventListener("keydown", (e) => {
    if (menuOpen && e.key === "Escape") closeMenu();
  }, true);

  addEventListener("scroll", () => current && place(), { capture: true, passive: true });
  addEventListener("resize", () => current && place(), { passive: true });
  document.addEventListener("fullscreenchange", () => hide(true));

  wrap.addEventListener("pointerleave", () => {
    if (!busy && !menuOpen) hideTimer = setTimeout(hide, 1200);
  });
  // Start asking OSDM for the qualities as soon as the pointer reaches the button, so the menu is ready sooner.
  main.addEventListener("pointerenter", () => {
    if (target?.kind === "video" && !busy) qualitiesFor(target);
  });
  main.addEventListener("click", (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (busy || !target) return;
    if (target.kind !== "video") send(target);
    else if (menuOpen) closeMenu();
    else openMenu();
  });
  close.addEventListener("click", (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (target) dismissed.add(target.url);
    hide(true);
  });
})();
