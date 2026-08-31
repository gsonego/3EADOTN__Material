// Module 6 -- the Assistant panel.
//
// Everything a student changes during this module happens in this panel, and
// all of it is stored in THIS BROWSER (localStorage). That is what lets a whole
// class share one deployed app while each person keeps their own prompt.
(() => {
  "use strict";

  // Same headers catalog.js uses -- the proxy needs to know which Catalog API
  // to call and with what key.
  const HEADER_BASE_URL = "X-Catalog-Base-Url";
  const HEADER_API_KEY = "X-Catalog-Api-Key";
  const KEY_SYSTEM_PROMPT = "catalog.assistant.systemPrompt";
  const KEY_GROUNDED = "catalog.assistant.grounded";
  const KEY_MEMORY = "catalog.assistant.memory";

  // The conversation. Kept in memory only: reload the page and the assistant
  // has forgotten you, which is itself worth pointing out in class.
  let history = [];

  const $ = (id) => document.getElementById(id);

  function authHeaders() {
    const headers = {};
    const baseUrl = localStorage.getItem("catalog.baseUrl") || "";
    const apiKey = localStorage.getItem("catalog.apiKey") || "";
    if (baseUrl) headers[HEADER_BASE_URL] = baseUrl;
    if (apiKey) headers[HEADER_API_KEY] = apiKey;
    return headers;
  }

  // ---------- settings, persisted per browser ----------

  function loadSettings() {
    $("assistant-system-prompt").value = localStorage.getItem(KEY_SYSTEM_PROMPT) || "";
    $("assistant-grounded").checked = localStorage.getItem(KEY_GROUNDED) === "true";
    $("assistant-memory").checked = localStorage.getItem(KEY_MEMORY) !== "false";
    renderConfigSummary();
  }

  function saveSettings() {
    localStorage.setItem(KEY_SYSTEM_PROMPT, $("assistant-system-prompt").value);
    localStorage.setItem(KEY_GROUNDED, $("assistant-grounded").checked ? "true" : "false");
    localStorage.setItem(KEY_MEMORY, $("assistant-memory").checked ? "true" : "false");
    renderConfigSummary();
  }

  function renderConfigSummary() {
    const custom = $("assistant-system-prompt").value.trim().length > 0;
    const grounded = $("assistant-grounded").checked;
    $("assistant-config-summary").textContent =
      `${custom ? "custom prompt" : "plain"}, ${grounded ? "with catalog" : "no catalog"}`;
  }

  // ---------- transcript ----------

  function escapeHtml(str) {
    return String(str ?? "").replace(/[&<>"']/g, (c) => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    }[c]));
  }

  function addMessage(who, text, cssClass) {
    const wrap = document.createElement("div");
    wrap.className = "assistant-msg " + (cssClass || "");
    wrap.innerHTML = `<div class="who">${escapeHtml(who)}</div><div class="bubble">${escapeHtml(text)}</div>`;
    const t = $("assistant-transcript");
    t.appendChild(wrap);
    t.scrollTop = t.scrollHeight;
    return wrap;
  }

  function renderStats(data, seconds) {
    // The whole point of showing this: promptTokens jumps the moment the
    // catalog is switched on. Context is not free, and here is the number.
    $("assistant-stats").textContent =
      `${data.usage.promptTokens} prompt + ${data.usage.completionTokens} completion tokens` +
      ` | ${data.grounded ? data.contextTitles + " titles sent" : "no catalog sent"}` +
      ` | ${seconds}s | ${data.model || ""}`;
  }

  // ---------- ask ----------

  async function ask() {
    const input = $("assistant-input");
    const question = input.value.trim();
    if (!question) return;

    if (!localStorage.getItem("catalog.baseUrl")) {
      addMessage("Error", "No Catalog API endpoint is set. Open the endpoint pill at the top first.", "err");
      return;
    }

    input.value = "";
    addMessage("You", question, "user");
    const pending = addMessage("Assistant", "Thinking…");

    const body = {
      question,
      systemPrompt: $("assistant-system-prompt").value.trim() || null,
      grounded: $("assistant-grounded").checked,
      history: $("assistant-memory").checked ? history : []
    };

    const started = performance.now();
    try {
      const res = await fetch("/api/assistant", {
        method: "POST",
        headers: { ...authHeaders(), "Content-Type": "application/json" },
        body: JSON.stringify(body)
      });

      const seconds = ((performance.now() - started) / 1000).toFixed(1);

      if (!res.ok) {
        pending.className = "assistant-msg err";
        pending.querySelector(".bubble").textContent =
          res.status === 404
            ? "This Catalog API has no /assistant endpoint yet (is it running v9?)."
            : `The assistant call failed (HTTP ${res.status}).`;
        return;
      }

      const data = await res.json();
      pending.querySelector(".bubble").textContent = data.answer;
      renderStats(data, seconds);

      // WE keep the history and WE resend it. The model itself remembers
      // nothing between requests.
      history.push({ role: "user", content: question });
      history.push({ role: "assistant", content: data.answer });
    } catch (err) {
      pending.className = "assistant-msg err";
      pending.querySelector(".bubble").textContent = `Couldn't reach the app's own proxy: ${err.message}`;
    }
  }

  // ---------- wiring ----------

  $("assistant-toggle").addEventListener("click", () => {
    $("assistant-drawer").classList.toggle("open");
    if ($("assistant-drawer").classList.contains("open")) $("assistant-input").focus();
  });
  $("assistant-close").addEventListener("click", () => $("assistant-drawer").classList.remove("open"));

  $("assistant-config-toggle").addEventListener("click", () =>
    $("assistant-config-body").classList.toggle("open"));

  $("assistant-send").addEventListener("click", ask);
  $("assistant-input").addEventListener("keydown", (e) => {
    if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); ask(); }
  });

  ["assistant-system-prompt", "assistant-grounded", "assistant-memory"].forEach(id =>
    $(id).addEventListener("change", saveSettings));
  $("assistant-system-prompt").addEventListener("input", saveSettings);

  $("assistant-reset").addEventListener("click", () => {
    history = [];
    $("assistant-transcript").innerHTML = "";
    $("assistant-stats").innerHTML = "&nbsp;";
  });

  loadSettings();
})();
