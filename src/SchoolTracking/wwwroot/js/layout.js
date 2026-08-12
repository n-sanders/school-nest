window.StLayout = (function () {
  const STORAGE_KEY = "schooltracking-layout";
  const LAYOUTS = ["default", "rail-left", "rail-right", "gallery"];
  const DEFAULT_LAYOUT = "default";

  const LABELS = {
    default: "Default",
    "rail-left": "Left",
    "rail-right": "Right",
    gallery: "Gallery",
  };

  function resolve(id) {
    // Migrate previous "stack" id
    if (id === "stack") return DEFAULT_LAYOUT;
    return LAYOUTS.includes(id) ? id : DEFAULT_LAYOUT;
  }

  function load() {
    try {
      return resolve(localStorage.getItem(STORAGE_KEY));
    } catch {
      return DEFAULT_LAYOUT;
    }
  }

  function save(id) {
    localStorage.setItem(STORAGE_KEY, resolve(id));
  }

  function apply(id) {
    const layout = resolve(id);
    document.documentElement.dataset.layout = layout;
    return layout;
  }

  function set(id) {
    const layout = apply(id);
    save(layout);
    syncSwitcher(layout);
    const menu = document.querySelector(".layout-menu");
    if (menu) menu.open = false;
    return layout;
  }

  function syncSwitcher(layout) {
    const root = document.querySelector(".layout-switcher");
    if (!root) return;
    root.querySelectorAll("[data-layout]").forEach((btn) => {
      const active = btn.dataset.layout === layout;
      btn.setAttribute("aria-pressed", active ? "true" : "false");
      btn.classList.toggle("is-active", active);
    });
  }

  function initSwitcher() {
    const root = document.querySelector(".layout-switcher");
    if (!root) return;

    if (!root.dataset.ready) {
      root.innerHTML = LAYOUTS.map(
        (id) =>
          `<button type="button" class="layout-switcher-btn" data-layout="${id}" aria-pressed="false">${LABELS[id]}</button>`
      ).join("");
      root.dataset.ready = "1";
      root.addEventListener("click", (e) => {
        const btn = e.target.closest("[data-layout]");
        if (!btn || !root.contains(btn)) return;
        set(btn.dataset.layout);
      });
    }

    syncSwitcher(load());
  }

  // Apply as soon as this script runs to limit FOUC.
  apply(load());

  return {
    STORAGE_KEY,
    LAYOUTS,
    DEFAULT_LAYOUT,
    LABELS,
    load,
    save,
    apply,
    set,
    initSwitcher,
  };
})();
