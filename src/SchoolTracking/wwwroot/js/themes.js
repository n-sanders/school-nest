window.StThemes = (function () {
  const COLOR_KEYS = [
    "bg",
    "main",
    "caret",
    "sub",
    "subAlt",
    "text",
    "error",
    "errorExtra",
    "colorfulError",
    "colorfulErrorExtra",
  ];

  const CSS_VAR_MAP = {
    bg: "--bg-color",
    main: "--main-color",
    caret: "--caret-color",
    sub: "--sub-color",
    subAlt: "--sub-alt-color",
    text: "--text-color",
    error: "--error-color",
    errorExtra: "--error-extra-color",
    colorfulError: "--colorful-error-color",
    colorfulErrorExtra: "--colorful-error-extra-color",
  };

  const COLOR_LABELS = {
    bg: "Background",
    main: "Main",
    caret: "Caret",
    sub: "Sub",
    subAlt: "Sub alt",
    text: "Text",
    error: "Error",
    errorExtra: "Error extra",
    colorfulError: "Colorful error",
    colorfulErrorExtra: "Colorful error extra",
  };

  const DEFAULT_THEME = "school";

  const themes = {
    school: {
      bg: "#f3efe6",
      main: "#2f6f4e",
      caret: "#1f4d36",
      sub: "#5c6b63",
      subAlt: "#e7efea",
      text: "#1f2a24",
      error: "#8b2e2e",
      errorExtra: "#6b2222",
      colorfulError: "#9a5b12",
      colorfulErrorExtra: "#7a480e",
    },
    midnight_ledger: {
      bg: "#12171c",
      main: "#3dba8c",
      caret: "#7ee0b8",
      sub: "#7a8a96",
      subAlt: "#1b2229",
      text: "#e8eef2",
      error: "#e35d5d",
      errorExtra: "#a33a3a",
      colorfulError: "#e35d5d",
      colorfulErrorExtra: "#a33a3a",
    },
    paper_ink: {
      bg: "#f7f4ee",
      main: "#1c2b3a",
      caret: "#c45c26",
      sub: "#6b7280",
      subAlt: "#ebe6dc",
      text: "#1c2b3a",
      error: "#b42318",
      errorExtra: "#7a180f",
      colorfulError: "#b42318",
      colorfulErrorExtra: "#7a180f",
    },
    blueberry: {
      bg: "#21252b",
      main: "#6b9bd1",
      caret: "#e2b714",
      sub: "#7f848e",
      subAlt: "#2a2f38",
      text: "#d7dae0",
      error: "#e06c75",
      errorExtra: "#be5046",
      colorfulError: "#e06c75",
      colorfulErrorExtra: "#be5046",
    },
    mint_cream: {
      bg: "#eef8f3",
      main: "#0f766e",
      caret: "#115e59",
      sub: "#4b6b63",
      subAlt: "#d7eee6",
      text: "#134e4a",
      error: "#b91c1c",
      errorExtra: "#7f1d1d",
      colorfulError: "#b91c1c",
      colorfulErrorExtra: "#7f1d1d",
    },
    dusk: {
      bg: "#2a2438",
      main: "#e8a87c",
      caret: "#f2c4a0",
      sub: "#9a90a8",
      subAlt: "#352e45",
      text: "#f3efe8",
      error: "#ef6f6c",
      errorExtra: "#b84340",
      colorfulError: "#ef6f6c",
      colorfulErrorExtra: "#b84340",
    },
    slate: {
      bg: "#e8ecf0",
      main: "#334155",
      caret: "#0ea5e9",
      sub: "#64748b",
      subAlt: "#d5dce4",
      text: "#0f172a",
      error: "#dc2626",
      errorExtra: "#991b1b",
      colorfulError: "#dc2626",
      colorfulErrorExtra: "#991b1b",
    },
    forest_night: {
      bg: "#0f1a14",
      main: "#86c49a",
      caret: "#c6e6c6",
      sub: "#6d8575",
      subAlt: "#16241b",
      text: "#e4f0e8",
      error: "#f07178",
      errorExtra: "#b84a50",
      colorfulError: "#f07178",
      colorfulErrorExtra: "#b84a50",
    },
    cotton_candy: {
      bg: "#fff0f6",
      main: "#db2777",
      caret: "#f472b6",
      sub: "#9d174d",
      subAlt: "#fce7f3",
      text: "#831843",
      error: "#be123c",
      errorExtra: "#9f1239",
      colorfulError: "#c026d3",
      colorfulErrorExtra: "#a21caf",
    },
    cherry_blossom: {
      bg: "#fff5f7",
      main: "#e11d48",
      caret: "#fb7185",
      sub: "#9f1239",
      subAlt: "#ffe4e6",
      text: "#4c0519",
      error: "#dc2626",
      errorExtra: "#b91c1c",
      colorfulError: "#ea580c",
      colorfulErrorExtra: "#c2410c",
    },
    lilac: {
      bg: "#f5f3ff",
      main: "#7c3aed",
      caret: "#a78bfa",
      sub: "#6d28d9",
      subAlt: "#ede9fe",
      text: "#2e1065",
      error: "#e11d48",
      errorExtra: "#be123c",
      colorfulError: "#d946ef",
      colorfulErrorExtra: "#c026d3",
    },
    grape_soda: {
      bg: "#1a1025",
      main: "#c4b5fd",
      caret: "#e9d5ff",
      sub: "#a78bfa",
      subAlt: "#2a1a3d",
      text: "#f3e8ff",
      error: "#fb7185",
      errorExtra: "#e11d48",
      colorfulError: "#f0abfc",
      colorfulErrorExtra: "#d946ef",
    },
    peach_fizz: {
      bg: "#fff7ed",
      main: "#ea580c",
      caret: "#fb923c",
      sub: "#c2410c",
      subAlt: "#ffedd5",
      text: "#7c2d12",
      error: "#dc2626",
      errorExtra: "#b91c1c",
      colorfulError: "#db2777",
      colorfulErrorExtra: "#be185d",
    },
  };

  const DEFAULT_FONT = "classic";

  const fonts = {
    classic: {
      display: '"Fraunces", Georgia, serif',
      body: '"Source Sans 3", "Segoe UI", sans-serif',
    },
    roundabout: {
      display: '"Fredoka", "Segoe UI", sans-serif',
      body: '"Nunito", "Segoe UI", sans-serif',
    },
    comic_book: {
      display: '"Comic Neue", "Comic Sans MS", cursive',
      body: '"Comic Neue", "Comic Sans MS", cursive',
    },
    bubble_gum: {
      display: '"Baloo 2", "Segoe UI", sans-serif',
      body: '"Quicksand", "Segoe UI", sans-serif',
    },
    scribbles: {
      display: '"Caveat", "Segoe UI", cursive',
      body: '"Nunito", "Segoe UI", sans-serif',
    },
  };

  function cloneColors(colors) {
    const out = {};
    for (const key of COLOR_KEYS) {
      out[key] = colors[key];
    }
    return out;
  }

  function getTheme(name) {
    const theme = themes[name];
    return theme ? cloneColors(theme) : null;
  }

  function getFont(name) {
    return fonts[name] || fonts[DEFAULT_FONT];
  }

  function displayName(name) {
    return String(name)
      .split("_")
      .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
      .join(" ");
  }

  return {
    COLOR_KEYS,
    CSS_VAR_MAP,
    COLOR_LABELS,
    DEFAULT_THEME,
    DEFAULT_FONT,
    themes,
    fonts,
    cloneColors,
    getTheme,
    getFont,
    displayName,
  };
})();
