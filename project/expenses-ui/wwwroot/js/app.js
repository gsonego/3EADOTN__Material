// Fixed category order + colors — validated as an adjacent-pair-safe sequence with the
// dataviz palette validator. Do not sort categories by amount: that would put
// unvalidated color pairs next to each other in the chart/table.
const CATEGORY_COLORS = {
  Groceries: "#2a78d6",
  Entertainment: "#eb6834",
  Restaurants: "#1baf7a",
  Transport: "#eda100",
  Utilities: "#e87ba4",
  Other: "#008300",
};
const CATEGORY_ORDER = Object.keys(CATEGORY_COLORS);

const bannersEl = document.getElementById("banners");
const categorySelect = document.getElementById("category");
const rowsEl = document.getElementById("expense-rows");
const barsEl = document.getElementById("category-bars");
const statTotalEl = document.getElementById("stat-total");
const statSubEl = document.getElementById("stat-sub");
const form = document.getElementById("expense-form");
const modalBackdrop = document.getElementById("modal-backdrop");

// ---------- localStorage-backed API endpoint settings ----------
// Same pattern as catalog-ui: the browser never talks to the Expenses API (or its
// APIM gateway) directly -- it only calls this server's own same-origin /proxy/*
// routes, sending the chosen base URL + subscription key as custom headers. This
// server attaches the key as Ocp-Apim-Subscription-Key server-side before forwarding.

const STORAGE_KEY_BASE_URL = "expenses.baseUrl";
const STORAGE_KEY_API_KEY = "expenses.apiKey";
const HEADER_BASE_URL = "X-Expenses-Base-Url";
const HEADER_API_KEY = "X-Expenses-Api-Key";

function getSettings() {
  return {
    baseUrl: localStorage.getItem(STORAGE_KEY_BASE_URL) || "",
    apiKey: localStorage.getItem(STORAGE_KEY_API_KEY) || "",
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

const endpointPill = document.getElementById("endpoint-pill");
const endpointDot = document.getElementById("endpoint-dot");
const endpointLabel = document.getElementById("endpoint-label");
const settingsModalBackdrop = document.getElementById("settings-modal-backdrop");
const settingsBaseUrlInput = document.getElementById("settings-base-url");
const settingsApiKeyInput = document.getElementById("settings-api-key");
const settingsStatusDot = document.getElementById("settings-status-dot");
const settingsStatusText = document.getElementById("settings-status-text");

function renderEndpointPill(status) {
  const { baseUrl } = getSettings();

  if (!baseUrl) {
    endpointDot.className = "endpoint-dot";
    endpointLabel.textContent = "No endpoint set";
    return;
  }

  let host = baseUrl;
  try { host = new URL(baseUrl).host; } catch { /* leave as typed */ }
  endpointLabel.textContent = host;
  endpointDot.className = "endpoint-dot" + (status === "ok" ? " ok" : status === "err" ? " err" : "");
}

function openSettingsModal() {
  const { baseUrl, apiKey } = getSettings();
  settingsBaseUrlInput.value = baseUrl;
  settingsApiKeyInput.value = apiKey;
  settingsStatusDot.className = "endpoint-dot";
  settingsStatusText.textContent = "Not tested yet";
  settingsModalBackdrop.hidden = false;
}
function closeSettingsModal() {
  settingsModalBackdrop.hidden = true;
}

endpointPill.addEventListener("click", openSettingsModal);
document.getElementById("close-settings-modal").addEventListener("click", closeSettingsModal);
document.getElementById("cancel-settings-btn").addEventListener("click", closeSettingsModal);
settingsModalBackdrop.addEventListener("click", (evt) => {
  if (evt.target === settingsModalBackdrop) closeSettingsModal();
});

document.getElementById("test-connection-btn").addEventListener("click", async () => {
  const baseUrl = settingsBaseUrlInput.value.trim();
  const apiKey = settingsApiKeyInput.value.trim();

  settingsStatusText.textContent = "Testing…";
  settingsStatusDot.className = "endpoint-dot";

  try {
    const res = await fetch("/proxy/health", {
      headers: { [HEADER_BASE_URL]: baseUrl, [HEADER_API_KEY]: apiKey },
    });
    if (res.ok) {
      settingsStatusDot.className = "endpoint-dot ok";
      settingsStatusText.textContent = "Connected";
    } else {
      settingsStatusDot.className = "endpoint-dot err";
      settingsStatusText.textContent = `Responded with HTTP ${res.status}`;
    }
  } catch (err) {
    settingsStatusDot.className = "endpoint-dot err";
    settingsStatusText.textContent = `Unreachable: ${err.message}`;
  }
});

document.getElementById("save-settings-btn").addEventListener("click", () => {
  const baseUrl = settingsBaseUrlInput.value.trim();
  const apiKey = settingsApiKeyInput.value.trim();
  saveSettings(baseUrl, apiKey);
  closeSettingsModal();
  loadExpenses();
});

function colorFor(category) {
  return CATEGORY_COLORS[category] ?? "#8b8a92";
}

function warningIcon() {
  return '<svg width="18" height="18" viewBox="0 0 20 20" fill="none"><path d="M10 2v8m0 4h.01M2.5 17h15L10 3 2.5 17Z" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round" stroke-linecap="round"/></svg>';
}

function addBanner(message, kind = "warning") {
  const div = document.createElement("div");
  div.className = `banner ${kind}`;
  div.innerHTML = `${warningIcon()}<span>${message}</span>`;
  bannersEl.appendChild(div);
}

function clearBanners() {
  bannersEl.innerHTML = "";
}

function populateCategories(names) {
  categorySelect.innerHTML = "";
  const ordered = CATEGORY_ORDER.filter((c) => names.includes(c)).concat(
    names.filter((c) => !CATEGORY_ORDER.includes(c))
  );
  for (const name of ordered.length ? ordered : names) {
    const opt = document.createElement("option");
    opt.value = name;
    opt.textContent = name;
    categorySelect.appendChild(opt);
  }
}

async function loadCategories() {
  try {
    const res = await fetch("/proxy/categories", { headers: authHeaders() });
    const names = await res.json();
    populateCategories(Array.isArray(names) && names.length ? names : CATEGORY_ORDER);
  } catch {
    populateCategories(CATEGORY_ORDER);
  }
}

function formatAmount(amount, currency) {
  return `€${Number(amount).toFixed(2)}`;
}

function deleteIcon() {
  return '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 6h18"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/></svg>';
}

function renderTable(items) {
  rowsEl.innerHTML = "";
  if (!items || items.length === 0) {
    rowsEl.innerHTML = '<tr><td colspan="6" class="empty">No expenses yet.</td></tr>';
    return;
  }
  const sorted = [...items].sort((a, b) => new Date(b.date) - new Date(a.date));
  for (const e of sorted) {
    const tr = document.createElement("tr");
    const dateStr = e.date ? new Date(e.date).toLocaleDateString() : "";
    const receiptCell = e.receiptPhotoUrl
      ? `<a href="${e.receiptPhotoUrl}" target="_blank" rel="noopener">View</a>`
      : '<span style="color:var(--ink-muted)">—</span>';
    tr.innerHTML = `
      <td>${dateStr}</td>
      <td><span class="category-dot" style="background:${colorFor(e.category)}"></span>${e.category ?? ""}</td>
      <td>${e.description ?? ""}</td>
      <td class="num amount-cell">${formatAmount(e.amount, e.currency)}</td>
      <td>${receiptCell}</td>
      <td class="actions-cell"><button class="icon-btn-delete" data-action="delete" data-id="${e.id}" data-category="${e.category}" title="Delete" type="button">${deleteIcon()}</button></td>
    `;
    rowsEl.appendChild(tr);
  }
}

rowsEl.addEventListener("click", async (evt) => {
  const btn = evt.target.closest('[data-action="delete"]');
  if (!btn) return;

  if (!confirm("Delete this expense?")) return;

  const id = btn.dataset.id;
  const category = btn.dataset.category;
  try {
    const res = await fetch(`/proxy/expenses/${encodeURIComponent(category)}/${encodeURIComponent(id)}`, {
      method: "DELETE",
      headers: authHeaders(),
    });
    if (res.ok || res.status === 404) {
      await loadExpenses();
    } else {
      const data = await res.json().catch(() => null);
      addBanner(data?.error || `Could not delete the expense (HTTP ${res.status}).`, "error");
    }
  } catch (err) {
    addBanner(`Could not delete the expense: ${err.message}`, "error");
  }
});

function renderChart(items) {
  const totals = {};
  for (const name of CATEGORY_ORDER) totals[name] = 0;
  for (const e of items ?? []) {
    if (e.category in totals) totals[e.category] += Number(e.amount) || 0;
  }
  const max = Math.max(1, ...Object.values(totals));

  barsEl.innerHTML = "";
  for (const name of CATEGORY_ORDER) {
    const amount = totals[name];
    const pct = Math.round((amount / max) * 100);
    const row = document.createElement("div");
    row.className = "chart-row";
    row.innerHTML = `
      <div class="chart-row-top">
        <span class="chart-row-name"><span class="category-dot" style="background:${colorFor(name)}"></span>${name}</span>
        <span class="chart-row-amount num">€${amount.toFixed(0)}</span>
      </div>
      <div class="chart-track"><div class="chart-fill" style="width:${pct}%;background:${colorFor(name)}"></div></div>
    `;
    barsEl.appendChild(row);
  }
}

function renderStat(items) {
  const now = new Date();
  const thisMonth = (items ?? []).filter((e) => {
    const d = new Date(e.date);
    return d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth();
  });
  const total = thisMonth.reduce((sum, e) => sum + (Number(e.amount) || 0), 0);
  const categoriesUsed = new Set(thisMonth.map((e) => e.category)).size;
  statTotalEl.textContent = `€${total.toFixed(2)}`;
  statSubEl.textContent = `across ${categoriesUsed} categor${categoriesUsed === 1 ? "y" : "ies"}`;
}

async function loadExpenses() {
  clearBanners();
  const { baseUrl } = getSettings();

  if (!baseUrl) {
    addBanner('No API endpoint configured yet. Click the endpoint pill above and set one.');
    renderEndpointPill(null);
    renderTable([]);
    renderChart([]);
    renderStat([]);
    return;
  }

  try {
    const res = await fetch("/proxy/expenses", { headers: authHeaders() });
    const data = await res.json();
    if (data.dataSourceConnected === false) {
      addBanner(data.message || "Data source not connected — showing no data.");
    }
    renderEndpointPill(res.ok && data.dataSourceConnected !== false ? "ok" : "err");
    renderTable(data.items ?? []);
    renderChart(data.items ?? []);
    renderStat(data.items ?? []);
  } catch (err) {
    renderEndpointPill("err");
    addBanner(`Could not load expenses: ${err.message}`, "error");
    renderTable([]);
    renderChart([]);
    renderStat([]);
  }
}

function openModal() {
  modalBackdrop.hidden = false;
}
function closeModal() {
  modalBackdrop.hidden = true;
  form.reset();
  document.getElementById("date").value = new Date().toISOString().slice(0, 10);
}

document.getElementById("open-modal").addEventListener("click", openModal);
document.getElementById("close-modal").addEventListener("click", closeModal);
document.getElementById("cancel-modal").addEventListener("click", closeModal);
modalBackdrop.addEventListener("click", (evt) => {
  if (evt.target === modalBackdrop) closeModal();
});

form.addEventListener("submit", async (evt) => {
  evt.preventDefault();
  clearBanners();

  const payload = {
    category: categorySelect.value,
    description: document.getElementById("description").value,
    amount: parseFloat(document.getElementById("amount").value),
    date: document.getElementById("date").value || null,
  };

  let created;
  try {
    const res = await fetch("/proxy/expenses", {
      method: "POST",
      headers: { ...authHeaders(), "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    const data = await res.json();
    if (!res.ok) {
      addBanner(data.error || "Could not save the expense.", "error");
      return;
    }
    created = data.item;
  } catch (err) {
    addBanner(`Could not save the expense: ${err.message}`, "error");
    return;
  }

  const photoFile = document.getElementById("photo").files[0];
  if (photoFile && created) {
    const fd = new FormData();
    fd.append("photo", photoFile);
    try {
      const res = await fetch(`/proxy/expenses/${created.category}/${created.id}/receipt`, {
        method: "POST",
        headers: authHeaders(),
        body: fd,
      });
      const data = await res.json();
      if (data.blobConnected === false) {
        addBanner(data.message || "Photo not stored — Blob Storage isn't connected yet.", "warning");
      }
    } catch (err) {
      addBanner(`Could not upload the receipt photo: ${err.message}`, "warning");
    }
  }

  closeModal();
  await loadExpenses();
});

(async function init() {
  document.getElementById("date").value = new Date().toISOString().slice(0, 10);
  renderEndpointPill(null);
  await loadCategories();
  await loadExpenses();
})();
