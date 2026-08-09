(async function () {
  const OPEN_KEY = "schooltracking-planner-open";
  const SLOTS = 5;

  const flash = document.getElementById("flash");
  const rowsEl = document.getElementById("childRows");
  const railEl = document.getElementById("subjectChips");
  const dialog = document.getElementById("pickerDialog");

  const me = await requireAuth("parent");
  await renderTopbar(me, "planner");

  const [students, tree] = await Promise.all([
    api.get("/api/catalog/students"),
    api.get("/api/catalog/tree")
  ]);

  // Only subjects that have something plannable become chips.
  const subjects = tree.filter(s => s.courses.some(c => c.assignments.length > 0));
  const subjectById = {};
  for (const s of tree) subjectById[s.id] = s;

  const daysByStudent = {};
  await Promise.all(students.map(async s => {
    daysByStudent[s.id] = await api.get(`/api/planner/${s.id}/days`);
  }));

  // ---- expand/collapse state (persisted) ----

  let openSet;
  try {
    openSet = new Set(JSON.parse(localStorage.getItem(OPEN_KEY) || "[]"));
  } catch {
    openSet = new Set();
  }
  const validIds = new Set(students.map(s => s.id));
  openSet = new Set([...openSet].filter(id => validIds.has(id)));
  if (openSet.size === 0 && students.length > 0) openSet.add(students[0].id);

  function persistOpen() {
    localStorage.setItem(OPEN_KEY, JSON.stringify([...openSet]));
  }

  function esc(value) {
    return String(value ?? "").replace(/[&<>"']/g, ch => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    })[ch]);
  }

  // ---- rendering ----

  function queueDays(studentId) {
    return (daysByStudent[studentId] || []).filter(d => d.status !== "completed");
  }

  function summaryText(studentId) {
    const days = queueDays(studentId);
    if (days.length === 0) return "No days planned";
    const count = days[0].assignments.filter(a => a.status !== "deferred").length;
    const dayWord = days.length === 1 ? "day" : "days";
    const asgWord = count === 1 ? "assignment" : "assignments";
    return `${days.length} ${dayWord} planned · next day has ${count} ${asgWord}`;
  }

  function assignmentItem(a, day) {
    const removable = day.status !== "completed";
    return `
      <li class="day-assignment">
        <div>
          <span class="day-assignment-course">${esc(a.subjectName)} · ${esc(a.courseName)}</span>
          <span class="day-assignment-name">${esc(a.name)}</span>
          <span class="muted day-assignment-effort">${esc(a.effort)}</span>
          ${a.status !== "assigned" ? statusBadge(a.status) : ""}
        </div>
        ${removable ? `<div class="day-assignment-actions">
          <button class="danger slim" data-remove="${a.id}" type="button" title="Remove assignment">✕</button>
        </div>` : ""}
      </li>`;
  }

  function dayCard(studentId, d) {
    const status = d.status === "inprogress" ? " · in progress" : "";
    const items = d.assignments.map(a => assignmentItem(a, d)).join("");
    return `
      <div class="day-slot planned" data-student="${studentId}" data-day="${d.id}">
        <h3>Day #${d.sequenceIndex}${status}</h3>
        <ul class="day-assignments">${items || `<li class="muted day-empty">Drop a subject here.</li>`}</ul>
      </div>`;
  }

  function dayStrip(studentId) {
    const days = queueDays(studentId).slice(0, SLOTS);
    const cells = days.map(d => dayCard(studentId, d));
    if (cells.length < SLOTS) {
      const maxSeq = (daysByStudent[studentId] || [])
        .reduce((m, d) => Math.max(m, d.sequenceIndex), 0);
      cells.push(`
        <button type="button" class="day-slot add" data-add-day="${studentId}">
          <span class="add-plus" aria-hidden="true">+</span>
          <span>Add day #${maxSeq + 1}</span>
        </button>`);
    }
    while (cells.length < SLOTS) {
      cells.push(`<div class="day-slot placeholder" aria-hidden="true"></div>`);
    }
    return `<div class="day-strip">${cells.join("")}</div>`;
  }

  function rowHtml(student) {
    const open = openSet.has(student.id);
    return `
      <section class="panel child-row" data-row="${student.id}">
        <button type="button" class="child-row-header" data-toggle="${student.id}" aria-expanded="${open}">
          <span class="chevron" aria-hidden="true">${open ? "▾" : "▸"}</span>
          <span class="child-row-name">${esc(student.displayName)}</span>
          <span class="child-row-summary muted">${summaryText(student.id)}</span>
        </button>
        <div class="child-row-body" ${open ? "" : "hidden"}>
          ${dayStrip(student.id)}
        </div>
      </section>`;
  }

  function renderRows() {
    rowsEl.innerHTML = students.map(rowHtml).join("") ||
      `<div class="panel muted">No students yet.</div>`;
  }

  function renderRow(studentId) {
    const student = students.find(s => s.id === studentId);
    const el = rowsEl.querySelector(`[data-row="${studentId}"]`);
    if (!student || !el) return;
    el.outerHTML = rowHtml(student);
  }

  async function refreshStudent(studentId) {
    daysByStudent[studentId] = await api.get(`/api/planner/${studentId}/days`);
    renderRow(studentId);
  }

  // ---- subject chips (drag source + click-to-arm fallback) ----

  let armedSubjectId = null;

  function renderChips() {
    railEl.innerHTML = subjects.map(s => `
      <button type="button" class="subject-chip${armedSubjectId === s.id ? " armed" : ""}"
              draggable="true" data-subject="${s.id}">${esc(s.name)}</button>
    `).join("") || `<p class="muted">No subjects with catalog assignments yet.</p>`;
  }

  railEl.addEventListener("click", e => {
    const chip = e.target.closest("[data-subject]");
    if (!chip) return;
    const id = Number(chip.dataset.subject);
    armedSubjectId = armedSubjectId === id ? null : id;
    renderChips();
  });

  railEl.addEventListener("dragstart", e => {
    const chip = e.target.closest("[data-subject]");
    if (!chip) return;
    e.dataTransfer.setData("text/plain", chip.dataset.subject);
    e.dataTransfer.effectAllowed = "copy";
  });

  document.addEventListener("keydown", e => {
    if (e.key === "Escape" && armedSubjectId !== null) {
      armedSubjectId = null;
      renderChips();
    }
  });

  // ---- row interactions ----

  rowsEl.addEventListener("click", async e => {
    const toggle = e.target.closest("[data-toggle]");
    if (toggle) {
      const id = Number(toggle.dataset.toggle);
      if (openSet.has(id)) openSet.delete(id); else openSet.add(id);
      persistOpen();
      renderRow(id);
      return;
    }

    const add = e.target.closest("[data-add-day]");
    if (add) {
      const studentId = Number(add.dataset.addDay);
      add.disabled = true;
      try {
        await api.post(`/api/planner/${studentId}/days`, {});
        await refreshStudent(studentId);
      } catch (err) {
        add.disabled = false;
        showFlash(flash, err.message, true);
      }
      return;
    }

    const remove = e.target.closest("[data-remove]");
    if (remove) {
      const card = remove.closest("[data-student]");
      try {
        await api.del(`/api/planner/assignments/${remove.dataset.remove}`);
        showFlash(flash, "Removed");
        await refreshStudent(Number(card.dataset.student));
      } catch (err) {
        showFlash(flash, err.message, true);
      }
      return;
    }

    // Armed chip: clicking a planned-day card acts like a drop.
    if (armedSubjectId !== null && !e.target.closest("button[data-remove], a")) {
      const card = e.target.closest(".day-slot.planned");
      if (card) {
        const subjectId = armedSubjectId;
        armedSubjectId = null;
        renderChips();
        openPicker(Number(card.dataset.student), Number(card.dataset.day), subjectId);
      }
    }
  });

  // ---- drag and drop onto planned-day cards ----

  rowsEl.addEventListener("dragover", e => {
    const card = e.target.closest(".day-slot.planned");
    if (!card) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = "copy";
    card.classList.add("drop-hot");
  });

  rowsEl.addEventListener("dragleave", e => {
    const card = e.target.closest(".day-slot.planned");
    if (card && !card.contains(e.relatedTarget)) card.classList.remove("drop-hot");
  });

  rowsEl.addEventListener("drop", e => {
    const card = e.target.closest(".day-slot.planned");
    if (!card) return;
    e.preventDefault();
    card.classList.remove("drop-hot");
    const subjectId = Number(e.dataTransfer.getData("text/plain"));
    if (!subjectId) return;
    openPicker(Number(card.dataset.student), Number(card.dataset.day), subjectId);
  });

  // ---- compact picker (subject known; choose course -> assignment -> effort) ----

  const pickerTitle = document.getElementById("pickerTitle");
  const pickerCourseLabel = document.getElementById("pickerCourseLabel");
  const pickerCourse = document.getElementById("pickerCourse");
  const pickerAssignmentLabel = document.getElementById("pickerAssignmentLabel");
  const pickerAssignment = document.getElementById("pickerAssignment");
  const pickerEffortLabel = document.getElementById("pickerEffortLabel");
  const pickerEffortWrap = document.getElementById("pickerEffortWrap");
  const pickerNote = document.getElementById("pickerNote");
  const pickerSubmit = document.getElementById("pickerSubmit");
  pickerEffortWrap.innerHTML = effortSelect("pickerEffort", "low");
  const pickerEffort = pickerEffortWrap.querySelector("select");

  let picker = null; // { studentId, dayId, subjectId }

  function openPicker(studentId, dayId, subjectId) {
    const subject = subjectById[subjectId];
    const student = students.find(s => s.id === studentId);
    const day = (daysByStudent[studentId] || []).find(d => d.id === dayId);
    if (!subject || !student || !day) return;

    picker = { studentId, dayId, subjectId };
    pickerTitle.textContent = `Add ${subject.name} — ${student.displayName}, Day #${day.sequenceIndex}`;

    // One required assignment per course per day (non-deferred): disable taken courses.
    const takenCourseIds = new Set(day.assignments
      .filter(a => a.kind === "required" && a.status !== "deferred")
      .map(a => a.courseId));

    const courses = subject.courses.filter(c => c.assignments.length > 0);
    pickerCourse.innerHTML = courses.map(c => {
      const taken = takenCourseIds.has(c.id);
      return `<option value="${c.id}" ${taken ? "disabled" : ""}>${esc(c.name)}${taken ? " — already planned" : ""}</option>`;
    }).join("");

    const firstAvailable = courses.find(c => !takenCourseIds.has(c.id));
    if (firstAvailable) pickerCourse.value = String(firstAvailable.id);

    // Single-course subjects skip the course step entirely.
    pickerCourseLabel.hidden = courses.length === 1 && !!firstAvailable;

    const usable = !!firstAvailable;
    pickerAssignmentLabel.hidden = !usable;
    pickerEffortLabel.hidden = !usable;
    pickerNote.hidden = usable;
    pickerSubmit.disabled = !usable;
    if (usable) {
      syncAssignments();
    } else {
      pickerNote.textContent = "Every course in this subject already has work on this day.";
      pickerAssignment.innerHTML = "";
    }
    dialog.showModal();
  }

  function syncAssignments() {
    const subject = subjectById[picker.subjectId];
    const course = subject.courses.find(c => c.id === Number(pickerCourse.value));
    pickerAssignment.innerHTML = (course ? course.assignments : [])
      .map(a => `<option value="${a.id}" data-effort="${a.defaultEffort}">${esc(a.name)}</option>`)
      .join("");
    syncEffort();
  }

  function syncEffort() {
    const opt = pickerAssignment.selectedOptions[0];
    if (opt) pickerEffort.value = opt.dataset.effort;
  }

  pickerCourse.addEventListener("change", syncAssignments);
  pickerAssignment.addEventListener("change", syncEffort);
  document.getElementById("pickerCancel").onclick = () => dialog.close();

  document.getElementById("pickerForm").onsubmit = async e => {
    e.preventDefault();
    if (!picker) return;
    const catalogAssignmentId = Number(pickerAssignment.value);
    if (!catalogAssignmentId) return;
    pickerSubmit.disabled = true;
    try {
      await api.post(`/api/planner/${picker.studentId}/days/${picker.dayId}/assignments`, {
        catalogAssignmentId,
        effort: pickerEffort.value
      });
      dialog.close();
      showFlash(flash, "Assigned");
      await refreshStudent(picker.studentId);
    } catch (err) {
      showFlash(flash, err.message, true);
    } finally {
      pickerSubmit.disabled = false;
    }
  };

  // ---- initial paint ----

  renderChips();
  renderRows();
})().catch(err => console.error(err));
