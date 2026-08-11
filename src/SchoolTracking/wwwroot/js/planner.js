(async function () {
  const OPEN_KEY = "schooltracking-planner-open";
  const LAST_KEY = "schooltracking-planner-last";
  const TEMPLATE_KEY = "schooltracking-planner-template";
  const COURSE_RAIL_KEY = "schooltracking-planner-courserail";
  const SLOTS = 5;

  const flash = document.getElementById("flash");
  const rowsEl = document.getElementById("childRows");
  const railEl = document.getElementById("subjectChips");
  const courseListEl = document.getElementById("courseList");
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

  // Flat lookup: catalog assignment id -> { subject, course, assignment }.
  const catalogById = {};
  for (const s of tree) {
    for (const c of s.courses) {
      for (const a of c.assignments) catalogById[a.id] = { subject: s, course: c, assignment: a };
    }
  }

  function readJson(key, fallback) {
    try {
      return JSON.parse(localStorage.getItem(key)) ?? fallback;
    } catch {
      return fallback;
    }
  }

  // ---- last course/assignment picked per kid + subject ----

  const lastPicks = readJson(LAST_KEY, {});

  function rememberPick(studentId, subjectId, courseId, catalogAssignmentId, effort) {
    (lastPicks[studentId] ??= {})[subjectId] = { courseId, catalogAssignmentId, effort };
    localStorage.setItem(LAST_KEY, JSON.stringify(lastPicks));
  }

  // ---- per-kid day-plan templates ----
  // Shape: { [studentId]: [{ catalogAssignmentId, effort }] }, pruned against the live catalog.

  const templates = readJson(TEMPLATE_KEY, {});
  for (const key of Object.keys(templates)) {
    const items = Array.isArray(templates[key]) ? templates[key] : [];
    templates[key] = items.filter(t => t && catalogById[t.catalogAssignmentId]);
  }

  function persistTemplates() {
    localStorage.setItem(TEMPLATE_KEY, JSON.stringify(templates));
  }

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
  let armedTemplate = false;
  const templateChip = document.getElementById("templateChip");

  function setArmedTemplate(value) {
    armedTemplate = value;
    templateChip.classList.toggle("armed", value);
  }

  function renderChips() {
    railEl.innerHTML = subjects.map(s => `
      <button type="button" class="subject-chip${armedSubjectId === s.id ? " armed" : ""}"
              draggable="true" data-subject="${s.id}">${esc(s.name)}</button>
    `).join("") || `<p class="muted">No subjects with catalog assignments yet.</p>`;
  }

  function renderCourses() {
    const blocks = tree
      .filter(s => s.courses.length > 0)
      .map(s => `
        <li class="course-group">
          <div class="course-group-heading">${esc(s.name)}</div>
          <ul class="course-group-list">
            ${s.courses.map(c => `<li>${esc(c.name)}</li>`).join("")}
          </ul>
        </li>`);
    courseListEl.innerHTML = blocks.join("") ||
      `<li class="muted">No courses yet. Add them in the catalog.</li>`;
  }

  railEl.addEventListener("click", e => {
    const chip = e.target.closest("[data-subject]");
    if (!chip) return;
    const id = Number(chip.dataset.subject);
    armedSubjectId = armedSubjectId === id ? null : id;
    setArmedTemplate(false);
    renderChips();
  });

  railEl.addEventListener("dragstart", e => {
    const chip = e.target.closest("[data-subject]");
    if (!chip) return;
    e.dataTransfer.setData("text/plain", chip.dataset.subject);
    e.dataTransfer.effectAllowed = "copy";
  });

  templateChip.addEventListener("click", () => {
    setArmedTemplate(!armedTemplate);
    if (armedTemplate && armedSubjectId !== null) {
      armedSubjectId = null;
      renderChips();
    }
  });

  templateChip.addEventListener("dragstart", e => {
    e.dataTransfer.setData("text/plain", "template");
    e.dataTransfer.effectAllowed = "copy";
  });

  document.addEventListener("keydown", e => {
    if (e.key !== "Escape") return;
    if (armedSubjectId !== null) {
      armedSubjectId = null;
      renderChips();
    }
    if (armedTemplate) setArmedTemplate(false);
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
    if ((armedSubjectId !== null || armedTemplate) && !e.target.closest("button[data-remove], a")) {
      const card = e.target.closest(".day-slot.planned");
      if (card) {
        const studentId = Number(card.dataset.student);
        const dayId = Number(card.dataset.day);
        if (armedTemplate) {
          setArmedTemplate(false);
          applyTemplate(studentId, dayId);
        } else {
          const subjectId = armedSubjectId;
          armedSubjectId = null;
          renderChips();
          openPicker(studentId, dayId, subjectId);
        }
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
    const data = e.dataTransfer.getData("text/plain");
    const studentId = Number(card.dataset.student);
    const dayId = Number(card.dataset.day);
    if (data === "template") {
      applyTemplate(studentId, dayId);
      return;
    }
    const subjectId = Number(data);
    if (!subjectId) return;
    openPicker(studentId, dayId, subjectId);
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

    // Default to the last pick for this kid + subject when still valid and available.
    const remembered = (lastPicks[studentId] || {})[subjectId];
    const rememberedCourse = remembered
      ? courses.find(c => c.id === remembered.courseId && !takenCourseIds.has(c.id))
      : null;
    const initialCourse = rememberedCourse || firstAvailable;
    if (initialCourse) pickerCourse.value = String(initialCourse.id);

    // Single-course subjects skip the course step entirely.
    pickerCourseLabel.hidden = courses.length === 1 && !!firstAvailable;

    const usable = !!firstAvailable;
    pickerAssignmentLabel.hidden = !usable;
    pickerEffortLabel.hidden = !usable;
    pickerNote.hidden = usable;
    pickerSubmit.disabled = !usable;
    if (usable) {
      syncAssignments();
      if (rememberedCourse && rememberedCourse.assignments.some(a => a.id === remembered.catalogAssignmentId)) {
        pickerAssignment.value = String(remembered.catalogAssignmentId);
        syncEffort();
        if (remembered.effort === "low" || remembered.effort === "high") {
          pickerEffort.value = remembered.effort;
        }
      }
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
      rememberPick(picker.studentId, picker.subjectId, Number(pickerCourse.value), catalogAssignmentId, pickerEffort.value);
      dialog.close();
      showFlash(flash, "Assigned");
      await refreshStudent(picker.studentId);
    } catch (err) {
      showFlash(flash, err.message, true);
    } finally {
      pickerSubmit.disabled = false;
    }
  };

  // ---- day-plan template: apply on drop ----

  async function applyTemplate(studentId, dayId) {
    const items = (templates[studentId] || []).filter(t => catalogById[t.catalogAssignmentId]);
    if (items.length === 0) {
      openTemplateEditor(studentId);
      return;
    }
    const day = (daysByStudent[studentId] || []).find(d => d.id === dayId);
    if (!day || day.status === "completed") return;

    const takenCourseIds = new Set(day.assignments
      .filter(a => a.kind === "required" && a.status !== "deferred")
      .map(a => a.courseId));

    let added = 0, skipped = 0, failed = 0;
    for (const item of items) {
      const courseId = catalogById[item.catalogAssignmentId].course.id;
      if (takenCourseIds.has(courseId)) {
        skipped += 1;
        continue;
      }
      try {
        await api.post(`/api/planner/${studentId}/days/${dayId}/assignments`, {
          catalogAssignmentId: item.catalogAssignmentId,
          effort: item.effort === "high" ? "high" : "low"
        });
        takenCourseIds.add(courseId);
        added += 1;
      } catch {
        failed += 1;
      }
    }
    await refreshStudent(studentId);

    const parts = [`Added ${added}`];
    if (skipped) parts.push(`skipped ${skipped} (already planned)`);
    if (failed) parts.push(`${failed} failed`);
    showFlash(flash, parts.join(" · "), failed > 0);
  }

  // ---- day-plan template: builder dialog ----

  const templateDialog = document.getElementById("templateDialog");
  const templateStudentSel = document.getElementById("templateStudent");
  const templateItemsEl = document.getElementById("templateItems");
  const templateNote = document.getElementById("templateNote");

  let editorStudentId = null;
  let editorItems = []; // working copy: [{ catalogAssignmentId, effort }]

  const plannableSubjects = tree.filter(s => s.courses.some(c => c.assignments.length > 0));

  function courseOptionsHtml(selectedCourseId) {
    return plannableSubjects.map(s => `
      <optgroup label="${esc(s.name)}">
        ${s.courses.filter(c => c.assignments.length > 0).map(c =>
          `<option value="${c.id}" ${c.id === selectedCourseId ? "selected" : ""}>${esc(c.name)}</option>`
        ).join("")}
      </optgroup>`).join("");
  }

  function templateRowHtml(item, idx) {
    const { course } = catalogById[item.catalogAssignmentId];
    return `
      <div class="template-item" data-idx="${idx}">
        <select data-role="course" aria-label="Course">${courseOptionsHtml(course.id)}</select>
        <select data-role="assignment" aria-label="Assignment">
          ${course.assignments.map(a =>
            `<option value="${a.id}" ${a.id === item.catalogAssignmentId ? "selected" : ""}>${esc(a.name)}</option>`
          ).join("")}
        </select>
        <select data-role="effort" aria-label="Effort">
          <option value="low" ${item.effort !== "high" ? "selected" : ""}>Low (30m)</option>
          <option value="high" ${item.effort === "high" ? "selected" : ""}>High (60m)</option>
        </select>
        <button type="button" class="danger slim" data-role="remove" title="Remove item">✕</button>
      </div>`;
  }

  function renderTemplateEditor() {
    templateItemsEl.innerHTML = editorItems.map(templateRowHtml).join("");
    templateNote.hidden = editorItems.length > 0;
    templateNote.textContent = "No items yet — add the courses this kid does every day.";
  }

  function loadEditorStudent(studentId) {
    editorStudentId = studentId;
    editorItems = (templates[studentId] || [])
      .filter(t => catalogById[t.catalogAssignmentId])
      .map(t => ({ catalogAssignmentId: t.catalogAssignmentId, effort: t.effort }));
    renderTemplateEditor();
  }

  function openTemplateEditor(studentId) {
    if (students.length === 0) return;
    templateStudentSel.innerHTML = students.map(s =>
      `<option value="${s.id}">${esc(s.displayName)}</option>`).join("");
    const target = students.some(s => s.id === studentId) ? studentId : students[0].id;
    templateStudentSel.value = String(target);
    loadEditorStudent(target);
    templateDialog.showModal();
  }

  templateStudentSel.addEventListener("change", () => {
    loadEditorStudent(Number(templateStudentSel.value));
  });

  document.getElementById("templateAddItem").onclick = () => {
    const usedCourseIds = new Set(editorItems.map(t => catalogById[t.catalogAssignmentId].course.id));
    const allCourses = plannableSubjects.flatMap(s => s.courses.filter(c => c.assignments.length > 0));
    const course = allCourses.find(c => !usedCourseIds.has(c.id)) || allCourses[0];
    if (!course) return;
    const first = course.assignments[0];
    editorItems.push({ catalogAssignmentId: first.id, effort: first.defaultEffort });
    renderTemplateEditor();
  };

  templateItemsEl.addEventListener("change", e => {
    const row = e.target.closest(".template-item");
    if (!row) return;
    const item = editorItems[Number(row.dataset.idx)];
    if (!item) return;
    const role = e.target.dataset.role;
    if (role === "course") {
      const course = plannableSubjects
        .flatMap(s => s.courses).find(c => c.id === Number(e.target.value));
      if (course && course.assignments.length > 0) {
        item.catalogAssignmentId = course.assignments[0].id;
        item.effort = course.assignments[0].defaultEffort;
      }
      renderTemplateEditor();
    } else if (role === "assignment") {
      const info = catalogById[Number(e.target.value)];
      if (info) {
        item.catalogAssignmentId = info.assignment.id;
        item.effort = info.assignment.defaultEffort;
      }
      renderTemplateEditor();
    } else if (role === "effort") {
      item.effort = e.target.value;
    }
  });

  templateItemsEl.addEventListener("click", e => {
    const remove = e.target.closest("[data-role='remove']");
    if (!remove) return;
    const row = remove.closest(".template-item");
    editorItems.splice(Number(row.dataset.idx), 1);
    renderTemplateEditor();
  });

  document.getElementById("templateForm").onsubmit = e => {
    e.preventDefault();
    if (editorStudentId === null) return;
    templates[editorStudentId] = editorItems.map(t => ({
      catalogAssignmentId: t.catalogAssignmentId,
      effort: t.effort
    }));
    persistTemplates();
    templateDialog.close();
    const student = students.find(s => s.id === editorStudentId);
    showFlash(flash, `Day plan saved${student ? ` for ${student.displayName}` : ""}`);
  };

  document.getElementById("templateClear").onclick = () => {
    if (editorStudentId === null) return;
    delete templates[editorStudentId];
    persistTemplates();
    editorItems = [];
    renderTemplateEditor();
  };

  document.getElementById("templateCancel").onclick = () => templateDialog.close();

  document.getElementById("editTemplateBtn").onclick = () => {
    const firstOpen = students.find(s => openSet.has(s.id));
    openTemplateEditor(firstOpen ? firstOpen.id : (students[0] && students[0].id));
  };

  // ---- course rail: collapsed by default, state persisted ----

  const courseRail = document.getElementById("courseRail");
  if (localStorage.getItem(COURSE_RAIL_KEY) === "open") courseRail.open = true;
  courseRail.addEventListener("toggle", () => {
    localStorage.setItem(COURSE_RAIL_KEY, courseRail.open ? "open" : "closed");
  });

  // ---- initial paint ----

  document.getElementById("addSubjectBtn").onclick = () => {
    location.href = "/catalog.html";
  };

  renderChips();
  renderCourses();
  renderRows();
})().catch(err => console.error(err));
