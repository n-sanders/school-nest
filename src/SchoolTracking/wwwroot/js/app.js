const api = {
  async request(path, options = {}) {
    const res = await fetch(path, {
      credentials: "same-origin",
      headers: {
        "Content-Type": "application/json",
        ...(options.headers || {})
      },
      ...options
    });
    if (res.status === 401) {
      if (!location.pathname.endsWith("/login.html")) {
        location.href = "/login.html";
      }
      throw new Error("Not authenticated");
    }
    const text = await res.text();
    let data = null;
    if (text) {
      try { data = JSON.parse(text); } catch { data = { raw: text }; }
    }
    if (!res.ok) {
      const msg = data?.error || res.statusText || "Request failed";
      throw new Error(msg);
    }
    return data;
  },
  get(path) { return this.request(path); },
  post(path, body) { return this.request(path, { method: "POST", body: JSON.stringify(body ?? {}) }); },
  put(path, body) { return this.request(path, { method: "PUT", body: JSON.stringify(body ?? {}) }); },
  patch(path, body) { return this.request(path, { method: "PATCH", body: JSON.stringify(body ?? {}) }); },
  del(path) { return this.request(path, { method: "DELETE" }); }
};

function effortSelect(name, selected = "low") {
  return `<select name="${name}">
    <option value="low" ${selected === "low" ? "selected" : ""}>Low (30m)</option>
    <option value="high" ${selected === "high" ? "selected" : ""}>High (60m)</option>
  </select>`;
}

/** Low/High switch for Today required-task cards. Checked = high. */
function effortToggle(name, selected = "low", { disabled = false } = {}) {
  const high = selected === "high";
  return `<label class="effort-toggle${disabled ? " is-disabled" : ""}">
    <span class="effort-toggle-label">Low</span>
    <input type="checkbox" class="effort-toggle-input" name="${name}" value="high"
      ${high ? "checked" : ""} ${disabled ? "disabled" : ""}
      role="switch" aria-checked="${high ? "true" : "false"}" />
    <span class="effort-toggle-track" aria-hidden="true"><span class="effort-toggle-thumb"></span></span>
    <span class="effort-toggle-label">High</span>
  </label>`;
}

function effortToggleValue(input) {
  return input?.checked ? "high" : "low";
}

function statusBadge(status) {
  const map = {
    assigned: ["Assigned", ""],
    completed: ["Completed", "done"],
    defer_requested: ["Defer requested", "defer"],
    deferred: ["Deferred", "pending"]
  };
  const [label, cls] = map[status] || [status, ""];
  return `<span class="badge ${cls}">${label}</span>`;
}

async function requireAuth(expectedRole) {
  const me = await api.get("/api/auth/me");
  if (expectedRole && me.role !== expectedRole) {
    location.href = me.role === "parent" ? "/planner.html" : "/index.html";
    throw new Error("Wrong role");
  }
  return me;
}

/** Decorative School Nest mark; pair with visible "School Nest" text for the accessible name. */
function snLogo(extraClass = "") {
  const cls = extraClass ? `schoolnest-mark ${extraClass}` : "schoolnest-mark";
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" class="${cls}" aria-hidden="true" focusable="false">
  <path class="sn-primary" d="M113 190C88 134 106 78 154 46c13 39 40 55 69 40 21-11 45-11 66 0 29 15 56-1 69-40 48 32 66 88 41 144 23 29 35 65 32 103-5 67-42 119-96 145-24 12-51 18-79 18s-55-6-79-18c-54-26-91-78-96-145-3-38 9-74 32-103Z"/>
  <path d="M128 269c-2 59 25 111 74 142-5-64-18-115-52-157-10 2-17 7-22 15Z" fill="var(--sn-ink, #25213a)" opacity=".16"/>
  <path d="M384 269c2 59-25 111-74 142 5-64 18-115 52-157 10 2 17 7 22 15Z" fill="var(--sn-ink, #25213a)" opacity=".16"/>
  <path class="sn-face" d="M256 284c-23 30-68 28-99-2-35-33-35-88-5-121 27-30 72-31 104-2 32-29 77-28 104 2 30 33 30 88-5 121-31 30-76 32-99 2Z"/>
  <circle cx="202" cy="211" r="43" fill="none" stroke="var(--sn-primary, #6557c8)" stroke-width="12"/>
  <circle cx="310" cy="211" r="43" fill="none" stroke="var(--sn-primary, #6557c8)" stroke-width="12"/>
  <circle class="sn-ink" cx="202" cy="211" r="22"/>
  <circle class="sn-ink" cx="310" cy="211" r="22"/>
  <circle class="sn-face" cx="194" cy="203" r="7"/>
  <circle class="sn-face" cx="302" cy="203" r="7"/>
  <path class="sn-accent" d="M256 226l25 20-25 32-25-32 25-20Z"/>
  <path class="sn-accent" d="M86 367c48-28 104-40 170-39 66-1 122 11 170 39-9 35-33 64-69 84-28 15-62 23-101 23s-73-8-101-23c-36-20-60-49-69-84Z"/>
  <path d="M91 367c45-30 103-43 165-42 62-1 120 12 165 42-43 23-98 34-165 34S134 390 91 367Z" fill="var(--sn-ink, #25213a)" opacity=".18"/>
  <path d="M111 407c42 25 90 37 145 37s103-12 145-37c-24 41-77 67-145 67s-121-26-145-67Z" fill="var(--sn-ink, #25213a)" opacity=".12"/>
  <path class="sn-detail" stroke-width="10" d="M211 354v24m-17-7 17 7 17-7M301 354v24m-17-7 17 7 17-7"/>
  <path class="sn-detail" stroke-width="15" opacity=".74" d="M92 369c47-24 102-34 164-33 62-1 117 9 164 33"/>
  <path class="sn-detail" stroke-width="12" opacity=".62" d="M99 359c48 24 102 34 162 30 61-4 113-18 155-42"/>
  <path class="sn-detail" stroke-width="12" opacity=".66" d="M106 392c47-23 101-31 159-26 58 4 105 21 141 48"/>
  <path class="sn-detail" stroke-width="11" opacity=".62" d="M121 420c47-23 99-30 154-24 50 5 89 19 116 40"/>
  <path class="sn-detail" stroke-width="10" opacity=".58" d="M157 449c31-21 69-30 112-26 37 3 68 13 91 29"/>
  <path class="sn-detail" stroke-width="10" opacity=".48" d="M111 378c39 36 89 59 151 70M401 377c-40 36-91 60-153 73"/>
  <path class="sn-detail" stroke-width="11" opacity=".7" d="M101 375 75 360M410 375l28-18"/>
</svg>`;
}

async function renderTopbar(me, active) {
  const themeLink = `<a href="/theme.html" class="${active === "theme" ? "active" : ""}">Theme</a>`;
  const parentLinks = `
    <a href="/planner.html" class="${active === "planner" ? "active" : ""}">Planner</a>
    <a href="/catalog.html" class="${active === "catalog" ? "active" : ""}">Catalog</a>
    <a href="/requests.html" class="${active === "requests" ? "active" : ""}" id="navRequests">Requests</a>
    <a href="/optional.html" class="${active === "optional" ? "active" : ""}">Optional</a>
    <a href="/reports.html" class="${active === "reports" ? "active" : ""}">Reports</a>
    <a href="/adjustments.html" class="${active === "adjustments" ? "active" : ""}">Adjustments</a>
    <a href="/magic-words.html" class="${active === "magic" ? "active" : ""}">Magic words</a>
    ${themeLink}
  `;
  const studentLinks = `
    <a href="/index.html" class="${active === "today" ? "active" : ""}">Today</a>
    ${themeLink}
  `;
  const el = document.getElementById("topbar");
  if (!el) return;
  el.innerHTML = `
    <div class="brand">${snLogo()}<span>School Nest</span></div>
    <div class="nav">
      ${me.role === "parent" ? parentLinks : studentLinks}
      <span class="muted">${me.displayName}</span>
      <button class="linkish" id="logoutBtn" type="button">Log out</button>
    </div>
  `;
  document.getElementById("logoutBtn").onclick = async () => {
    await api.post("/api/auth/logout", {});
    location.href = "/login.html";
  };

  if (me.role === "parent") {
    try {
      const { count } = await api.get("/api/assignments/requests/count");
      const link = document.getElementById("navRequests");
      if (link && count > 0) {
        link.innerHTML = `Requests <span class="nav-count">${count}</span>`;
      }
    } catch { /* leave nav without badge */ }
  }
}

function showFlash(el, message, isError = false) {
  if (!el) return;
  el.className = `flash${isError ? " error" : ""}`;
  el.textContent = message;
  el.hidden = false;
}
