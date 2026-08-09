(async function () {
  const me = await requireAuth();
  await renderTopbar(me, "theme");

  document.getElementById("previewBrand").innerHTML =
    `${snLogo()}<span>School Nest</span>`;

  const presetGrid = document.getElementById("preset-grid");
  const fontGrid = document.getElementById("font-grid");
  const colorGrid = document.getElementById("color-grid");
  let state = StTheme.loadState();
  let draftColors = StThemes.cloneColors(StTheme.resolveColors(state));

  function normalizeHex(value) {
    const v = String(value || "").trim();
    if (/^#[0-9a-fA-F]{6}$/.test(v)) return v.toLowerCase();
    if (/^#[0-9a-fA-F]{3}$/.test(v)) {
      return (
        "#" +
        v
          .slice(1)
          .split("")
          .map((c) => c + c)
          .join("")
          .toLowerCase()
      );
    }
    return null;
  }

  function renderPresets() {
    presetGrid.innerHTML = "";
    for (const [name, colors] of Object.entries(StThemes.themes)) {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "theme-preset";
      if (state.mode === "preset" && state.name === name) {
        btn.classList.add("is-active");
      }
      btn.dataset.theme = name;
      btn.innerHTML = `
        <div class="theme-preset-swatches" aria-hidden="true">
          <span style="background:${colors.bg}"></span>
          <span style="background:${colors.main}"></span>
          <span style="background:${colors.text}"></span>
          <span style="background:${colors.sub}"></span>
        </div>
        <div class="theme-preset-name">${StThemes.displayName(name)}</div>
      `;
      btn.addEventListener("click", () => {
        state = StTheme.setPreset(name);
        draftColors = StThemes.cloneColors(state.colors);
        syncColorInputs();
        renderPresets();
      });
      presetGrid.appendChild(btn);
    }
  }

  function renderFonts() {
    fontGrid.innerHTML = "";
    for (const [name, font] of Object.entries(StThemes.fonts)) {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "theme-preset theme-font-preset";
      if (state.font === name) {
        btn.classList.add("is-active");
      }
      btn.dataset.font = name;
      btn.innerHTML = `
        <div class="theme-font-sample" style="font-family:${font.display}">Aa</div>
        <div class="theme-preset-name" style="font-family:${font.display}">${StThemes.displayName(name)}</div>
      `;
      btn.addEventListener("click", () => {
        state = StTheme.setFont(name);
        renderFonts();
      });
      fontGrid.appendChild(btn);
    }
  }

  function syncColorInputs() {
    for (const key of StThemes.COLOR_KEYS) {
      const colorInput = document.getElementById(`color-${key}`);
      const hexInput = document.getElementById(`hex-${key}`);
      if (!colorInput || !hexInput) continue;
      const value = draftColors[key];
      colorInput.value = value;
      hexInput.value = value;
    }
  }

  function updateCustomColor(key, rawValue) {
    const hex = normalizeHex(rawValue);
    if (!hex) return;
    draftColors[key] = hex;
    state = StTheme.setCustom(draftColors);
    syncColorInputs();
    renderPresets();
  }

  function renderColorEditor() {
    colorGrid.innerHTML = "";
    for (const key of StThemes.COLOR_KEYS) {
      const field = document.createElement("div");
      field.className = "theme-color-field";
      field.innerHTML = `
        <label for="color-${key}">${StThemes.COLOR_LABELS[key]}</label>
        <div class="theme-color-row">
          <input type="color" id="color-${key}" value="${draftColors[key]}" data-key="${key}" />
          <input type="text" id="hex-${key}" value="${draftColors[key]}" data-key="${key}" maxlength="7" spellcheck="false" />
        </div>
      `;
      colorGrid.appendChild(field);
    }

    colorGrid.querySelectorAll('input[type="color"]').forEach((input) => {
      input.addEventListener("input", () => {
        updateCustomColor(input.dataset.key, input.value);
      });
    });

    colorGrid.querySelectorAll('input[type="text"]').forEach((input) => {
      input.addEventListener("change", () => {
        const hex = normalizeHex(input.value);
        if (!hex) {
          input.value = draftColors[input.dataset.key];
          return;
        }
        updateCustomColor(input.dataset.key, hex);
      });
    });
  }

  renderPresets();
  renderFonts();
  renderColorEditor();
})();
