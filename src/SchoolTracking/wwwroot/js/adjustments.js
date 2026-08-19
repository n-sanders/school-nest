(() => {
  let selectedDayId = null;
  let loading = false;

  function dayStatusBadge(status) {
    const map = {
      planned: ["Planned", ""],
      inprogress: ["In progress", "pending"],
      completed: ["Completed", "done"],
      partiallycompleted: ["Partial", "pending"]
    };
    const key = String(status || "").replace(/_/g, "").toLowerCase();
    const [label, cls] = map[key] || [status, ""];
    return `<span class="badge ${cls}">${label}</span>`;
  }

  function normalizeDayStatus(status) {
    return String(status || "").toLowerCase();
  }

  async function loadPage({ keepDayId = true } = {}) {
    if (loading) return;
    loading = true;
    const flash = document.getElementById("flash");
    try {
      const me = await requireAuth("parent");
      await renderTopbar(me, "adjustments");

      const students = await api.get("/api/catalog/students");
      const studentSelect = document.getElementById("studentId");
      const prevStudent = studentSelect.value;
      studentSelect.innerHTML = students.map(s =>
        `<option value="${s.id}">${s.displayName}</option>`
      ).join("");
      if (prevStudent) studentSelect.value = prevStudent;

      const around = document.getElementById("around");
      if (!around.value) around.value = new Date().toISOString().slice(0, 10);

      const studentId = studentSelect.value;
      if (!studentId) return;

      const dayIdParam = keepDayId && selectedDayId ? `&dayId=${selectedDayId}` : "";
      const data = await api.get(
        `/api/corrections/${studentId}/days?around=${around.value}${dayIdParam}`
      );

      selectedDayId = data.selectedDayId ?? null;
      renderDayPicker(data.days || [], selectedDayId);
      renderDay(data.day);
    } catch (err) {
      showFlash(flash, err.message, true);
    } finally {
      loading = false;
    }
  }

  function renderDayPicker(days, activeId) {
    const list = document.getElementById("dayPicker");
    if (!days.length) {
      list.innerHTML = `<li class="muted">No planned days found for this student.</li>`;
      return;
    }

    list.innerHTML = days.map(d => {
      const active = d.id === activeId ? " is-active" : "";
      const dateLabel = d.calendarDate || "No calendar date";
      return `<li class="adjust-day-pick${active}">
        <button type="button" data-pick-day="${d.id}">
          <span><strong>Day #${d.sequenceIndex}</strong> · ${dateLabel}</span>
          <span class="muted">${d.completedCount}/${d.assignmentCount} done</span>
          ${dayStatusBadge(d.status)}
        </button>
      </li>`;
    }).join("");
  }

  function renderDay(day) {
    const panel = document.getElementById("dayPanel");
    if (!day) {
      panel.hidden = true;
      return;
    }
    panel.hidden = false;
    selectedDayId = day.id;

    document.getElementById("dayTitle").textContent = `Day #${day.sequenceIndex}`;
    document.getElementById("dayMeta").textContent =
      [
        day.startedOn ? `Started ${day.startedOn}` : null,
        day.completedAt
          ? `Completed at ${new Date(day.completedAt).toLocaleString()}`
          : `Status: ${day.status}`
      ].filter(Boolean).join(" · ");

    const statusEl = document.getElementById("dayStatusBadge");
    const statusKey = normalizeDayStatus(day.status).replace(/_/g, "");
    const statusMap = {
      planned: ["Planned", ""],
      inprogress: ["In progress", "pending"],
      completed: ["Completed", "done"],
      partiallycompleted: ["Partial", "pending"]
    };
    const [statusLabel, statusCls] = statusMap[statusKey] || [day.status, ""];
    statusEl.textContent = statusLabel;
    statusEl.className = `badge ${statusCls}`.trim();

    const closed = statusKey === "completed" || statusKey === "partiallycompleted";
    const done = statusKey === "completed";
    const dayDone = document.getElementById("dayDone");
    const dateInput = document.getElementById("dayCalendarDate");
    dayDone.checked = done;
    dateInput.value = day.calendarDate || "";
    dateInput.disabled = !closed;
    document.getElementById("saveDayDate").disabled = !closed;
    document.getElementById("reopenDay").hidden = statusKey !== "partiallycompleted";

    const list = document.getElementById("assignmentList");
    const items = day.assignments || [];
    list.innerHTML = items.map(a => {
      const canToggle = a.status === "assigned" || a.status === "completed";
      const isDone = a.status === "completed";
      const dateDisabled = !isDone ? "disabled" : "";
      return `<li data-assignment-id="${a.id}">
        <div class="adjust-asg-head">
          <div>
            <strong>${a.subjectName || "—"} · ${a.courseName || "—"}</strong>
            <div>${a.name}</div>
            <div class="muted">${a.kind} · ${a.effort}</div>
          </div>
          ${statusBadge(a.status)}
          ${carryoverBadge(a.carryoverKind, a.sourceStartedOn)}
        </div>
        <div class="row adjust-asg-controls">
          <label class="adjust-toggle">
            <span>Done</span>
            <input type="checkbox" data-asg-done="${a.id}"
              ${isDone ? "checked" : ""} ${canToggle ? "" : "disabled"} />
          </label>
          <label>Activity date
            <input type="date" data-asg-date="${a.id}" value="${a.activityDate || ""}" ${dateDisabled} />
          </label>
          <button class="secondary slim" type="button" data-asg-save-date="${a.id}" ${dateDisabled}>Save date</button>
        </div>
      </li>`;
    }).join("") || `<li class="muted">No assignments on this day.</li>`;
  }

  async function patchDay(body) {
    const flash = document.getElementById("flash");
    try {
      const day = await api.patch(`/api/corrections/days/${selectedDayId}`, body);
      showFlash(flash, "Day updated");
      renderDay(day);
      await loadPage({ keepDayId: true });
    } catch (err) {
      showFlash(flash, err.message, true);
      await loadPage({ keepDayId: true });
    }
  }

  async function patchAssignment(id, body) {
    const flash = document.getElementById("flash");
    try {
      await api.patch(`/api/corrections/assignments/${id}`, body);
      showFlash(flash, "Assignment updated");
      await loadPage({ keepDayId: true });
    } catch (err) {
      showFlash(flash, err.message, true);
      await loadPage({ keepDayId: true });
    }
  }

  document.getElementById("filters").onsubmit = async (e) => {
    e.preventDefault();
    selectedDayId = null;
    await loadPage({ keepDayId: false });
  };

  document.getElementById("dayPicker").onclick = async (e) => {
    const btn = e.target.closest("[data-pick-day]");
    if (!btn) return;
    selectedDayId = Number(btn.dataset.pickDay);
    await loadPage({ keepDayId: true });
  };

  document.getElementById("dayDone").onchange = async (e) => {
    const checked = e.target.checked;
    const around = document.getElementById("around").value;
    const dateInput = document.getElementById("dayCalendarDate");
    if (checked) {
      const calendarDate = dateInput.value || around;
      dateInput.value = calendarDate;
      dateInput.disabled = false;
      await patchDay({ completed: true, calendarDate });
    } else {
      await patchDay({ completed: false });
    }
  };

  document.getElementById("reopenDay").onclick = async () => {
    await patchDay({ completed: false });
  };

  document.getElementById("saveDayDate").onclick = async () => {
    const calendarDate = document.getElementById("dayCalendarDate").value;
    if (!calendarDate) {
      showFlash(document.getElementById("flash"), "Pick a calendar date first", true);
      return;
    }
    await patchDay({ calendarDate });
  };

  document.getElementById("assignmentList").onchange = async (e) => {
    const t = e.target;
    if (t.dataset.asgDone) {
      await patchAssignment(Number(t.dataset.asgDone), {
        completed: t.checked,
        ...(t.checked
          ? { activityDate: document.querySelector(`[data-asg-date="${t.dataset.asgDone}"]`)?.value
              || document.getElementById("around").value }
          : {})
      });
    }
  };

  document.getElementById("assignmentList").onclick = async (e) => {
    const t = e.target;
    if (!t.dataset.asgSaveDate) return;
    const id = t.dataset.asgSaveDate;
    const input = document.querySelector(`[data-asg-date="${id}"]`);
    await patchAssignment(Number(id), { activityDate: input?.value || "" });
  };

  loadPage({ keepDayId: false });
})();
