(() => {
  "use strict";

  const STORAGE_KEY_BASE_URL = "catalog.baseUrl";
  const STORAGE_KEY_API_KEY = "catalog.apiKey";
  const HEADER_BASE_URL = "X-Catalog-Base-Url";
  const HEADER_API_KEY = "X-Catalog-Api-Key";

  const GENRE_COLORS = {
    Action: "#3987e5",
    Comedy: "#d95926",
    Documentary: "#199e70",
    Drama: "#c98500",
    "Sci-Fi": "#d55181",
    Other: "#55555a"
  };

  const titlesById = new Map();
  let activeGenre = "";

  // ---------- localStorage-backed endpoint settings ----------

  function getSettings() {
    return {
      baseUrl: localStorage.getItem(STORAGE_KEY_BASE_URL) || "",
      apiKey: localStorage.getItem(STORAGE_KEY_API_KEY) || ""
    };
  }

  function saveSettings(baseUrl, apiKey) {
    localStorage.setItem(STORAGE_KEY_BASE_URL, baseUrl);
    localStorage.setItem(STORAGE_KEY_API_KEY, apiKey);
  }

  function authHeaders() {
    const { baseUrl, apiKey } = getSettings();
    const headers = {};
    if (baseUrl) headers[HEADER_BASE_URL] = baseUrl;
    if (apiKey) headers[HEADER_API_KEY] = apiKey;
    return headers;
  }

  function renderEndpointPill(status) {
    const { baseUrl } = getSettings();
    const dot = document.getElementById("endpoint-dot");
    const label = document.getElementById("endpoint-label");

    if (!baseUrl) {
      dot.className = "endpoint-dot";
      label.textContent = "No endpoint set";
      return;
    }

    let host = baseUrl;
    try { host = new URL(baseUrl).host; } catch { /* leave as typed */ }
    label.textContent = host;
    dot.className = "endpoint-dot" + (status === "ok" ? " ok" : status === "err" ? " err" : "");
  }

  // ---------- modal plumbing ----------

  function openModal(id) { document.getElementById(id).classList.add("open"); }
  function closeModal(id) { document.getElementById(id).classList.remove("open"); }

  document.addEventListener("click", (e) => {
    const closeTarget = e.target.closest("[data-close-modal]");
    if (closeTarget) closeModal(closeTarget.getAttribute("data-close-modal"));
    if (e.target.classList.contains("modal-backdrop-custom")) e.target.classList.remove("open");
  });

  // ---------- title count pill (Module 4, Topic 1 -- Caching) ----------

  async function loadTitleCount() {
    const dot = document.getElementById("count-dot");
    const label = document.getElementById("count-label");
    const pill = document.getElementById("count-pill");

    try {
      const res = await fetch("/api/titles/count", { headers: authHeaders() });
      if (!res.ok) { label.textContent = "-- titles"; dot.className = "endpoint-dot"; return; }

      const data = await res.json();
      label.textContent = `${data.count} title${data.count === 1 ? "" : "s"}`;
      // HIT (green) = served from IMemoryCache, 0 RU. MISS (amber) = a real
      // Cosmos DB query just ran -- requestCharge shows what it cost.
      dot.className = "endpoint-dot" + (data.cacheStatus === "HIT" ? " ok" : " warn");
      pill.title = data.cacheStatus === "HIT"
        ? `Served from cache (0 RU) -- cached for ${data.ttlSeconds}s`
        : `Fresh from Cosmos DB (${data.requestCharge} RU) -- now cached for ${data.ttlSeconds}s`;
    } catch {
      label.textContent = "-- titles";
      dot.className = "endpoint-dot";
    }
  }

  // ---------- catalog grid ----------

  async function loadTitles() {
    const grid = document.getElementById("catalog-grid");
    const { baseUrl } = getSettings();

    if (!baseUrl) {
      grid.innerHTML = `<div class="empty-state">No API endpoint configured yet. Open the endpoint pill above and set one.</div>`;
      renderEndpointPill(null);
      return;
    }

    grid.innerHTML = `<div class="empty-state">Loading…</div>`;

    try {
      const res = await fetch("/api/titles", { headers: authHeaders() });
      if (!res.ok) {
        renderEndpointPill(res.status === 404 ? null : "err");
        grid.innerHTML = res.status === 404
          ? `<div class="empty-state">Connected, but this API doesn't support listing titles yet.</div>`
          : `<div class="error-state">Couldn't load the catalog (HTTP ${res.status}).</div>`;
        return;
      }

      renderEndpointPill("ok");
      const titles = await res.json();
      titlesById.clear();
      (titles || []).forEach(t => titlesById.set(t.id, t));
      renderGrid();
      loadTitleCount();
    } catch (err) {
      renderEndpointPill("err");
      grid.innerHTML = `<div class="error-state">Couldn't reach the app's own API proxy: ${escapeHtml(err.message)}</div>`;
    }
  }

  function renderGrid() {
    const grid = document.getElementById("catalog-grid");
    const items = [...titlesById.values()].filter(t => !activeGenre || t.genre === activeGenre);

    if (items.length === 0) {
      grid.innerHTML = `<div class="empty-state">No titles yet. Add one to get started.</div>`;
      return;
    }

    grid.innerHTML = items.map(renderCard).join("");
  }

  function renderCard(t) {
    const color = GENRE_COLORS[t.genre] || GENRE_COLORS.Other;
    const bg = t.posterUrl
      ? `background-image:url('${escapeAttr(t.posterUrl)}'); background-size:cover; background-position:center;`
      : `background:linear-gradient(155deg, ${color}33 0%, #0d0d0d 100%);`;

    return `
      <div class="poster-card" style="${bg}" data-id="${escapeAttr(t.id)}">
        <div class="overlay"></div>
        <div class="actions">
          <button class="iconbtn" data-action="edit" data-id="${escapeAttr(t.id)}" title="Edit">
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z"/></svg>
          </button>
          <button class="iconbtn danger" data-action="delete" data-id="${escapeAttr(t.id)}" title="Delete">
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 6h18"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/></svg>
          </button>
        </div>
        <div class="poster-body">
          <span class="chip-inline" style="background:${color};">${escapeHtml(t.genre || "Other")}</span>
          <div class="poster-title">${escapeHtml(t.title || "Untitled")}</div>
          <div class="poster-meta">${t.year || ""}</div>
        </div>
      </div>`;
  }

  document.getElementById("catalog-grid").addEventListener("click", (e) => {
    const btn = e.target.closest("[data-action]");
    if (!btn) return;
    const id = btn.getAttribute("data-id");
    if (btn.getAttribute("data-action") === "edit") openTitleModal(titlesById.get(id));
    if (btn.getAttribute("data-action") === "delete") deleteTitle(id);
  });

  document.getElementById("genre-chips").addEventListener("click", (e) => {
    const chip = e.target.closest(".chip");
    if (!chip) return;
    document.querySelectorAll("#genre-chips .chip").forEach(c => c.classList.remove("active"));
    chip.classList.add("active");
    activeGenre = chip.getAttribute("data-genre") || "";
    renderGrid();
  });

  // ---------- add/edit title modal ----------

  let selectedPosterFile = null;

  function openTitleModal(existing) {
    selectedPosterFile = null;
    document.getElementById("title-form-error").style.display = "none";
    document.getElementById("title-modal-heading").textContent = existing ? "EDIT TITLE" : "ADD TITLE";
    document.getElementById("title-id").value = existing?.id || "";
    document.getElementById("title-name").value = existing?.title || "";
    document.getElementById("title-genre").value = existing?.genre || "Action";
    document.getElementById("title-year").value = existing?.year || new Date().getFullYear();
    document.getElementById("title-description").value = existing?.description || "";

    const preview = document.getElementById("poster-preview");
    preview.style.backgroundImage = existing?.posterUrl ? `url('${existing.posterUrl}')` : "";

    openModal("title-modal-backdrop");
  }

  document.getElementById("add-title-btn").addEventListener("click", () => openTitleModal(null));

  document.getElementById("poster-preview").addEventListener("click", () => {
    document.getElementById("poster-file-input").click();
  });

  document.getElementById("poster-file-input").addEventListener("change", (e) => {
    const file = e.target.files[0];
    if (!file) return;
    selectedPosterFile = file;
    const reader = new FileReader();
    reader.onload = () => {
      document.getElementById("poster-preview").style.backgroundImage = `url('${reader.result}')`;
    };
    reader.readAsDataURL(file);
  });

  document.getElementById("title-save-btn").addEventListener("click", async () => {
    const errorBox = document.getElementById("title-form-error");
    errorBox.style.display = "none";

    const id = document.getElementById("title-id").value;
    const payload = {
      title: document.getElementById("title-name").value.trim(),
      genre: document.getElementById("title-genre").value,
      year: parseInt(document.getElementById("title-year").value, 10) || null,
      description: document.getElementById("title-description").value.trim()
    };

    if (!payload.title) {
      errorBox.textContent = "Title is required.";
      errorBox.style.display = "block";
      return;
    }

    try {
      const url = id ? `/api/titles/${encodeURIComponent(id)}` : "/api/titles";
      const method = id ? "PUT" : "POST";
      const res = await fetch(url, {
        method,
        headers: { ...authHeaders(), "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });

      if (!res.ok) {
        errorBox.textContent = res.status === 404
          ? "Connected, but this API doesn't support saving titles yet."
          : `Save failed (HTTP ${res.status}).`;
        errorBox.style.display = "block";
        return;
      }

      const saved = await res.json().catch(() => null);
      const savedId = saved?.id || id;

      if (selectedPosterFile && savedId) {
        const formData = new FormData();
        formData.append("file", selectedPosterFile);
        await fetch(`/api/titles/${encodeURIComponent(savedId)}/poster`, {
          method: "POST",
          headers: authHeaders(),
          body: formData
        });
      }

      closeModal("title-modal-backdrop");
      loadTitles();
    } catch (err) {
      errorBox.textContent = `Couldn't reach the app's own API proxy: ${err.message}`;
      errorBox.style.display = "block";
    }
  });

  async function deleteTitle(id) {
    if (!confirm("Delete this title?")) return;
    try {
      const res = await fetch(`/api/titles/${encodeURIComponent(id)}`, {
        method: "DELETE",
        headers: authHeaders()
      });
      if (res.ok || res.status === 404) loadTitles();
    } catch { /* connection issue; leave the grid as-is */ }
  }

  // ---------- endpoint settings modal ----------

  document.getElementById("endpoint-pill").addEventListener("click", () => {
    const { baseUrl, apiKey } = getSettings();
    document.getElementById("settings-base-url").value = baseUrl;
    document.getElementById("settings-api-key").value = apiKey;
    document.getElementById("settings-status-dot").className = "endpoint-dot";
    document.getElementById("settings-status-text").textContent = "Not tested yet";
    openModal("settings-modal-backdrop");
  });

  document.getElementById("test-connection-btn").addEventListener("click", async () => {
    const dot = document.getElementById("settings-status-dot");
    const text = document.getElementById("settings-status-text");
    const baseUrl = document.getElementById("settings-base-url").value.trim();
    const apiKey = document.getElementById("settings-api-key").value.trim();

    text.textContent = "Testing…";
    dot.className = "endpoint-dot";

    try {
      const res = await fetch("/api/health", {
        headers: { [HEADER_BASE_URL]: baseUrl, [HEADER_API_KEY]: apiKey }
      });
      if (res.ok) {
        dot.className = "endpoint-dot ok";
        text.textContent = "Connected";
      } else {
        dot.className = "endpoint-dot err";
        text.textContent = `Responded with HTTP ${res.status}`;
      }
    } catch (err) {
      dot.className = "endpoint-dot err";
      text.textContent = `Unreachable: ${err.message}`;
    }
  });

  document.getElementById("settings-save-btn").addEventListener("click", () => {
    const baseUrl = document.getElementById("settings-base-url").value.trim();
    const apiKey = document.getElementById("settings-api-key").value.trim();
    saveSettings(baseUrl, apiKey);
    closeModal("settings-modal-backdrop");
    loadTitles();
  });

  // ---------- helpers ----------

  function escapeHtml(str) {
    return String(str ?? "").replace(/[&<>"']/g, (c) => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    }[c]));
  }
  function escapeAttr(str) { return escapeHtml(str); }

  // ---------- boot ----------

  renderEndpointPill(null);
  loadTitles();
})();
