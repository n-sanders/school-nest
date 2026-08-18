(() => {
  const flash = document.getElementById("flash");
  const studentSelect = document.getElementById("studentId");
  const weekPrev = document.getElementById("weekPrev");
  const weekNext = document.getElementById("weekNext");
  const weekLabel = document.getElementById("weekLabel");
  const weekStats = document.getElementById("weekStats");
  const weekDays = document.getElementById("weekDays");

  let weekStart = startOfWeekSunday(new Date());

  function toDateOnly(date) {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, "0");
    const d = String(date.getDate()).padStart(2, "0");
    return `${y}-${m}-${d}`;
  }

  function parseDateOnly(iso) {
    const [y, m, d] = String(iso || "").split("-").map(Number);
    return new Date(y, (m || 1) - 1, d || 1);
  }

  function addDays(date, n) {
    const d = new Date(date.getFullYear(), date.getMonth(), date.getDate());
    d.setDate(d.getDate() + n);
    return d;
  }

  function startOfWeekSunday(date) {
    const d = new Date(date.getFullYear(), date.getMonth(), date.getDate());
    d.setDate(d.getDate() - d.getDay());
    return d;
  }

  function isCurrentWeek(start) {
    return toDateOnly(start) === toDateOnly(startOfWeekSunday(new Date()));
  }

  function formatWeekLabel(startIso, endIso) {
    const start = parseDateOnly(startIso);
    const end = parseDateOnly(endIso);
    const startStr = start.toLocaleDateString("en-US", { month: "short", day: "numeric" });
    const endStr = end.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
    return `${startStr} – ${endStr}`;
  }

  function formatDayHeading(dateIso, weekday) {
    const date = parseDateOnly(dateIso);
    const label = date.toLocaleDateString("en-US", { month: "short", day: "numeric" });
    return `${weekday}, ${label}`;
  }

  function itemMeta(a) {
    const mins = `${a.minutes ?? 0}m`;
    if (a.kind === "optional" && !a.countsTowardHours) return `${mins} · awaiting hours`;
    if (a.kind === "optional") return `${mins} · optional`;
    return mins;
  }

  function renderWeek(week) {
    weekStart = parseDateOnly(week.start);
    weekLabel.textContent = formatWeekLabel(week.start, week.end);
    weekNext.disabled = isCurrentWeek(weekStart);

    weekStats.innerHTML = `
      <div class="stat"><div class="muted">Items</div><div class="value">${week.itemCount}</div></div>
      <div class="stat"><div class="muted">Hours</div><div class="value">${week.totalHours}</div></div>
    `;

    weekDays.innerHTML = (week.days || []).map(day => {
      const items = day.items || [];
      const list = items.length
        ? `<ul class="week-items">${items.map(a => `
            <li class="week-item">
              <span>
                <span class="week-item-subject">${escapeHtml(a.subjectName || "")}</span>
                · ${escapeHtml(a.name || "")}
              </span>
              <span class="week-item-meta">${escapeHtml(itemMeta(a))}</span>
            </li>`).join("")}</ul>`
        : `<p class="muted week-empty">No items</p>`;
      return `
        <div class="week-day">
          <h3>${escapeHtml(formatDayHeading(day.date, day.weekday))}</h3>
          ${list}
        </div>`;
    }).join("");
  }

  async function loadWeek() {
    const studentId = studentSelect.value;
    if (!studentId) return;
    const week = await api.get(`/api/reports/${studentId}/week?start=${toDateOnly(weekStart)}`);
    renderWeek(week);
  }

  async function loadReport() {
    const me = await requireAuth("parent");
    await renderTopbar(me, "reports");

    const students = await api.get("/api/catalog/students");
    const prev = studentSelect.value;
    studentSelect.innerHTML = students.map(s =>
      `<option value="${s.id}">${escapeHtml(s.displayName)}</option>`
    ).join("");
    if (prev) studentSelect.value = prev;

    const family = await api.get("/api/reports/family");
    document.getElementById("targetHours").value = family.targetHoursPerYear;

    const to = document.getElementById("to");
    const from = document.getElementById("from");
    if (!to.value) to.value = toDateOnly(new Date());
    if (!from.value) {
      const d = new Date();
      d.setFullYear(d.getFullYear() - 1);
      d.setDate(d.getDate() + 1);
      from.value = toDateOnly(d);
    }

    const studentId = studentSelect.value;
    if (!studentId) return;

    const [report] = await Promise.all([
      api.get(`/api/reports/${studentId}?from=${from.value}&to=${to.value}`),
      loadWeek()
    ]);
    document.getElementById("stats").innerHTML = `
      <div class="stat"><div class="muted">Total hours</div><div class="value">${report.totalHours}</div></div>
      <div class="stat"><div class="muted">Target</div><div class="value">${report.targetHoursPerYear}</div></div>
      <div class="stat"><div class="muted">Remaining</div><div class="value">${report.hoursRemaining}</div></div>
      <div class="stat"><div class="muted">Full days</div><div class="value">${report.fullDayCount}</div></div>
      <div class="stat"><div class="muted">Partial days</div><div class="value">${report.partialDayCount ?? 0}</div></div>
      <div class="stat"><div class="muted">Active days</div><div class="value">${report.activeDayCount}</div></div>
    `;

    document.getElementById("calendar").innerHTML = report.calendar.map(d => `
      <tr>
        <td>${d.date}</td>
        <td>${d.isFullDay ? "Full" : d.isPartialDay ? "Partial" : "—"}</td>
        <td>${d.hours}</td>
        <td>${d.requiredMinutes}</td>
        <td>${d.optionalMinutes}</td>
        <td>${d.assignmentCount}</td>
      </tr>
    `).join("") || `<tr><td colspan="6" class="muted">No activity in range.</td></tr>`;
  }

  document.getElementById("filters").onsubmit = async (e) => {
    e.preventDefault();
    try { await loadReport(); }
    catch (err) { showFlash(flash, err.message, true); }
  };

  document.getElementById("targetForm").onsubmit = async (e) => {
    e.preventDefault();
    try {
      await api.put("/api/reports/family/target-hours", {
        targetHoursPerYear: Number(document.getElementById("targetHours").value)
      });
      showFlash(flash, "Target saved");
      await loadReport();
    } catch (err) {
      showFlash(flash, err.message, true);
    }
  };

  studentSelect.onchange = async () => {
    try { await loadReport(); }
    catch (err) { showFlash(flash, err.message, true); }
  };

  weekPrev.onclick = async () => {
    weekStart = addDays(weekStart, -7);
    try { await loadWeek(); }
    catch (err) { showFlash(flash, err.message, true); }
  };

  weekNext.onclick = async () => {
    if (isCurrentWeek(weekStart)) return;
    weekStart = addDays(weekStart, 7);
    if (toDateOnly(weekStart) > toDateOnly(startOfWeekSunday(new Date()))) {
      weekStart = startOfWeekSunday(new Date());
    }
    try { await loadWeek(); }
    catch (err) { showFlash(flash, err.message, true); }
  };

  loadReport().catch(() => {});
})();
