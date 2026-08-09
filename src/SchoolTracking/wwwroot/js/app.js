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

async function renderTopbar(me, active) {
  const themeLink = `<a href="/theme.html" class="${active === "theme" ? "active" : ""}">Theme</a>`;
  const parentLinks = `
    <a href="/planner.html" class="${active === "planner" ? "active" : ""}">Planner</a>
    <a href="/catalog.html" class="${active === "catalog" ? "active" : ""}">Catalog</a>
    <a href="/requests.html" class="${active === "requests" ? "active" : ""}" id="navRequests">Requests</a>
    <a href="/optional.html" class="${active === "optional" ? "active" : ""}">Optional</a>
    <a href="/reports.html" class="${active === "reports" ? "active" : ""}">Reports</a>
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
    <div class="brand">SchoolTracking</div>
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
