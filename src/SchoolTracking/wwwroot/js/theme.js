window.StTheme = (function () {
  const STORAGE_KEY = "schooltracking-theme";

  function parseHex(hex) {
    const v = String(hex || "").trim();
    if (/^#[0-9a-fA-F]{6}$/.test(v)) {
      return [
        parseInt(v.slice(1, 3), 16),
        parseInt(v.slice(3, 5), 16),
        parseInt(v.slice(5, 7), 16),
      ];
    }
    if (/^#[0-9a-fA-F]{3}$/.test(v)) {
      return [
        parseInt(v[1] + v[1], 16),
        parseInt(v[2] + v[2], 16),
        parseInt(v[3] + v[3], 16),
      ];
    }
    return null;
  }

  function channelToLinear(c) {
    const s = c / 255;
    return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  }

  function relativeLuminance(hex) {
    const rgb = parseHex(hex);
    if (!rgb) return 1;
    const [r, g, b] = rgb.map(channelToLinear);
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  }

  function applyLogoTokens(colors) {
    const root = document.documentElement;
    const dark = relativeLuminance(colors.bg) < 0.45;
    root.style.setProperty(
      "--sn-face",
      dark
        ? `color-mix(in srgb, ${colors.main} 14%, #fff9ee)`
        : colors.bg
    );
    root.style.setProperty("--sn-ink", "#25213a");
  }

  function applyTheme(colors) {
    const root = document.documentElement;
    const map = StThemes.CSS_VAR_MAP;
    for (const key of StThemes.COLOR_KEYS) {
      if (colors[key]) {
        root.style.setProperty(map[key], colors[key]);
      }
    }
    applyLogoTokens(colors);
  }

  function applyFont(name) {
    const font = StThemes.getFont(name);
    const root = document.documentElement;
    root.style.setProperty("--font-display", font.display);
    root.style.setProperty("--font-body", font.body);
  }

  function resolveFontName(name) {
    if (name && StThemes.fonts[name]) return name;
    return StThemes.DEFAULT_FONT;
  }

  function defaultState() {
    return {
      mode: "preset",
      name: StThemes.DEFAULT_THEME,
      colors: StThemes.getTheme(StThemes.DEFAULT_THEME),
      font: StThemes.DEFAULT_FONT,
    };
  }

  function loadState() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return defaultState();
      const parsed = JSON.parse(raw);
      const font = resolveFontName(parsed.font);
      if (parsed.mode === "custom" && parsed.colors) {
        const colors = StThemes.cloneColors({
          ...StThemes.getTheme(StThemes.DEFAULT_THEME),
          ...parsed.colors,
        });
        return { mode: "custom", colors, font };
      }
      if (parsed.mode === "preset" && parsed.name && StThemes.themes[parsed.name]) {
        return {
          mode: "preset",
          name: parsed.name,
          colors: StThemes.getTheme(parsed.name),
          font,
        };
      }
    } catch {
      /* ignore corrupt storage */
    }
    return defaultState();
  }

  function saveState(state) {
    const font = resolveFontName(state.font);
    const payload =
      state.mode === "custom"
        ? { mode: "custom", colors: StThemes.cloneColors(state.colors), font }
        : { mode: "preset", name: state.name, font };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(payload));
  }

  function resolveColors(state) {
    if (state.mode === "custom" && state.colors) {
      return StThemes.cloneColors(state.colors);
    }
    return StThemes.getTheme(state.name || StThemes.DEFAULT_THEME);
  }

  function applySaved() {
    const state = loadState();
    applyTheme(resolveColors(state));
    applyFont(state.font);
    return state;
  }

  function setPreset(name) {
    if (!StThemes.themes[name]) return null;
    const current = loadState();
    const state = {
      mode: "preset",
      name,
      colors: StThemes.getTheme(name),
      font: resolveFontName(current.font),
    };
    applyTheme(state.colors);
    saveState(state);
    return state;
  }

  function setCustom(colors) {
    const current = loadState();
    const state = {
      mode: "custom",
      colors: StThemes.cloneColors(colors),
      font: resolveFontName(current.font),
    };
    applyTheme(state.colors);
    saveState(state);
    return state;
  }

  function setFont(name) {
    if (!StThemes.fonts[name]) return null;
    const current = loadState();
    const state = {
      ...current,
      colors: resolveColors(current),
      font: name,
    };
    applyFont(name);
    saveState(state);
    return state;
  }

  function getActiveColors() {
    return resolveColors(loadState());
  }

  // Apply as soon as this script runs to limit FOUC.
  applySaved();

  return {
    STORAGE_KEY,
    applyTheme,
    applyFont,
    loadState,
    saveState,
    applySaved,
    setPreset,
    setCustom,
    setFont,
    getActiveColors,
    resolveColors,
  };
})();
