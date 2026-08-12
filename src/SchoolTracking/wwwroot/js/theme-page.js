(async function () {
  const me = await requireAuth();
  await renderTopbar(me, "theme");

  document.getElementById("previewBrand").innerHTML =
    `${snLogo()}<span>School Nest</span>`;

  document.getElementById("previewEffortAssigned").innerHTML = effortToggle("preview-effort-a", "low");
  document.getElementById("previewEffortDone").innerHTML = effortToggle("preview-effort-b", "high");
  document.getElementById("previewEffortOptional").innerHTML = effortToggle("preview-effort-opt", "low");

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

  if (me.role !== "student") return;

  const bgPanel = document.getElementById("bg-panel");
  const bgQuota = document.getElementById("bg-quota");
  const bgForm = document.getElementById("bg-form");
  const bgPrompt = document.getElementById("bg-prompt");
  const bgGenerate = document.getElementById("bg-generate");
  const bgStatus = document.getElementById("bg-status");
  const bgClearWrap = document.getElementById("bg-clear-wrap");
  const bgClear = document.getElementById("bg-clear");
  const bgGallery = document.getElementById("bg-gallery");
  const previewSample = document.getElementById("previewSample");
  const flash = document.getElementById("flash");
  let bgState = null;

  function applyPreviewBackground(id) {
    if (!previewSample) return;
    if (id) {
      previewSample.classList.add("has-ai-bg");
      previewSample.style.setProperty("--page-bg-image", `url("/api/backgrounds/${id}/image")`);
    } else {
      previewSample.classList.remove("has-ai-bg");
      previewSample.style.removeProperty("--page-bg-image");
    }
  }

  function setBgStatus(message, show) {
    bgStatus.hidden = !show;
    bgStatus.textContent = message || "";
  }

  function formatCountdown(seconds) {
    const s = Math.max(0, seconds);
    const m = Math.floor(s / 60);
    const r = s % 60;
    return `${m}:${String(r).padStart(2, "0")}`;
  }

  function startGenerateCountdown() {
    let left = bgState?.generateTimeoutSeconds ?? 120;
    setBgStatus(`Generating… ${formatCountdown(left)} remaining`, true);
    return setInterval(() => {
      left = Math.max(0, left - 1);
      setBgStatus(`Generating… ${formatCountdown(left)} remaining`, true);
    }, 1000);
  }

  function renderBackgrounds() {
    if (!bgState) return;
    bgPanel.hidden = false;
    const remaining = bgState.remainingToday ?? 0;
    const limit = bgState.dailyLimit ?? 0;
    if (!bgState.configured) {
      bgQuota.textContent = "Ask a parent to turn this on.";
      bgPrompt.disabled = true;
      bgGenerate.disabled = true;
    } else {
      bgQuota.textContent = remaining === 1
        ? `1 generation left today (${limit} per day).`
        : `${remaining} generations left today (${limit} per day).`;
      bgPrompt.disabled = remaining <= 0;
      bgGenerate.disabled = remaining <= 0;
    }

    applyPreviewBackground(bgState.activeBackgroundId);
    bgClearWrap.hidden = !bgState.activeBackgroundId;

    const images = bgState.images || [];
    if (!images.length) {
      bgGallery.innerHTML = `<p class="muted">No backgrounds yet.</p>`;
      return;
    }

    bgGallery.innerHTML = images.map((img) => {
      const active = img.id === bgState.activeBackgroundId;
      return `<button type="button" class="bg-card${active ? " is-active" : ""}" data-id="${img.id}">
        <img src="${escapeHtml(img.imageUrl)}" alt="" loading="lazy" />
        <span class="bg-card-prompt">${escapeHtml(img.studentPrompt)}</span>
        ${active ? `<span class="bg-card-used">On Today</span>` : ""}
      </button>`;
    }).join("");
  }

  async function refreshBackgrounds() {
    bgState = await api.get("/api/backgrounds");
    renderBackgrounds();
  }

  bgForm.addEventListener("submit", async (e) => {
    e.preventDefault();
    const prompt = bgPrompt.value.trim();
    if (!prompt) {
      showFlash(flash, "Describe the background you want", true);
      return;
    }
    bgGenerate.disabled = true;
    let tick = null;
    try {
      tick = startGenerateCountdown();
      await api.post("/api/backgrounds", { prompt });
      bgPrompt.value = "";
      showFlash(flash, "Background created and applied to Today");
      await refreshBackgrounds();
    } catch (err) {
      showFlash(flash, err.message, true);
    } finally {
      if (tick) clearInterval(tick);
      setBgStatus("", false);
      renderBackgrounds();
    }
  });

  bgGallery.addEventListener("click", async (e) => {
    const btn = e.target.closest("[data-id]");
    if (!btn || !bgGallery.contains(btn)) return;
    const id = Number(btn.dataset.id);
    try {
      await api.put("/api/backgrounds/active", { id });
      showFlash(flash, "Applied to Today");
      await refreshBackgrounds();
    } catch (err) {
      showFlash(flash, err.message, true);
    }
  });

  bgClear.addEventListener("click", async () => {
    try {
      await api.put("/api/backgrounds/active", { id: null });
      showFlash(flash, "Background cleared");
      await refreshBackgrounds();
    } catch (err) {
      showFlash(flash, err.message, true);
    }
  });

  try {
    await refreshBackgrounds();
  } catch (err) {
    showFlash(flash, err.message, true);
  }
})();
