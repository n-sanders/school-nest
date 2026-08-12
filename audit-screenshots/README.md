# School Nest — Feature audit walkthrough

School Nest is a self-hosted homeschool tracker: parents build a catalog and ordered day slots; students complete or defer work and log optional hours; parents approve requests and track compliance hours.

Screenshots in this folder were captured from a live local run (1920×1080) against the Development DB. At capture time the catalog was seeded, several students had planned days, Evie’s current day was completed, and Requests showed one pending optional-hours acknowledgment.

| File | Page |
|------|------|
| `01-login.png` | Login |
| `02-planner.png` | Planner (parent) |
| `03-catalog.png` | Catalog (parent) |
| `04-requests.png` | Requests (parent) |
| `05-optional.png` | Optional (parent) |
| `06-reports.png` | Reports (parent) |
| — | Adjustments (parent) — no screenshot yet |
| `07-magic-words.png` | Magic words (parent) |
| `08-theme.png` | Theme (parent nav) |
| `09-today-student.png` | Today (student) |
| `10-theme-student-nav.png` | Theme (student nav) |

---

## Roles & navigation

| Role | Landing | Nav |
|------|---------|-----|
| Parent | `/planner.html` | Planner, Catalog, Requests, Optional, Reports, Adjustments, Magic words, Theme |
| Student | `/index.html` | Today, Theme (Layout switcher on Today) |

Auth is profile pick + magic word. `/deferrals.html` redirects to `/requests.html`.

---

## Cross-cutting concepts

- **Effort:** Low = 30m, High = 60m. Set on assign. Students can change it on Today (on complete, and after). Parents can change it on Requests for pending optional hours. There is no general parent UI for past required-work effort.
- **Planned days:** Ordered slots (not calendar-first). A day completes when all required work is done.
- **Day-plan templates:** Per-kid shortcuts in the planner, stored in that browser’s localStorage (not shared across parents or devices).
- **Optional work:** Separate from subjects/courses. Hours count only after parent acknowledgment on Requests.
- **Deferral:** Student requests → parent approves (slides course forward) or rejects.

---

## End-to-end workflows

1. **Curriculum setup (parent):** Catalog → add Subject → Course → catalog assignment (name, URL, default effort, description).
2. **Schedule required work (parent):** Planner → expand a student → Add day → drag/click a subject (or course) onto a day card → pick assignment + effort in the dialog (MVP: one required assignment per course per day). Optional: **edit plans** to save a per-kid day-plan template, then drop **Day plan** onto a day to fill it.
3. **Do school (student):** Today → Complete (with effort) or Request deferral; optionally expand Optional work → list or freeform + effort → Add & complete.
4. **Parent inbox:** Requests → approve/reject deferrals; acknowledge optional hours (optionally adjust effort first).
5. **Parent logs optional for a kid:** Optional → student + activity + effort + date → Add completed optional → still needs Requests ack for hours.
6. **Compliance:** Reports → date range + student → hours vs family yearly target; day calendar of full/active days.
7. **Fix dates / completion (parent):** Adjustments → pick student + around date → toggle day or assignment complete, or change calendar/activity date. Day and assignment toggles are independent.
8. **Access:** Magic words — parent can change own + all students; cannot change other parents’ words.
9. **Look & feel:** Theme — fonts, presets, custom colors + live preview; device-local.

---

## Pages

### 1. Login — `/login.html`

**Purpose:** Shared family gate; pick person, enter magic word.

**Workflow:** Choose profile → magic word → Enter. Parents → Planner; students → Today. Already logged in redirects by role.

![Login](01-login.png)

---

### 2. Planner (parent) — `/planner.html`

**Purpose:** Build each student’s ordered day slots and attach required catalog work.

**Workflow:** Subject/course rails on the left; per-student day rows on the right. Add a day card, then drag a subject onto it (or click subject, then day) and choose course, assignment, and effort. Empty days say “Drop a subject here.”

**Day-plan templates:** Edit plans (per kid) in this browser, then drag or click **Day plan** onto a day to apply that kid’s usual courses. Templates are localStorage only.

![Planner](02-planner.png)

---

### 3. Catalog (parent) — `/catalog.html`

**Purpose:** Family curriculum library (Subject → Course → Assignment templates).

**Workflow:** Browse tree → Add subject / course / assignment. Planner pulls from here; not per-student.

![Catalog](03-catalog.png)

---

### 4. Requests (parent) — `/requests.html`

**Purpose:** Approval inbox (nav badge when count > 0).

**Workflows:**

- **Deferrals:** Approve slide / Reject
- **Optional hours:** Set effort (optional) → Acknowledge hours

![Requests](04-requests.png)

---

### 5. Optional (parent) — `/optional.html`

**Purpose:** Parent logs completed optional work for a student.

**Workflow:** Student + list item or freeform name + effort + date → Add completed optional → still needs Requests ack. Freeform names join the family optional list. Seeded list includes Free reading, Nature walk, Extra practice.

![Optional](05-optional.png)

---

### 6. Reports (parent) — `/reports.html`

**Purpose:** Compliance vs family yearly hour target.

**Workflow:** Student + from/to → Refresh. Stats: total / target / remaining / full days / active days. Day calendar: date, full day?, hours, required/optional minutes, item count. Save family target hours/year.

![Reports](06-reports.png)

---

### 7. Adjustments (parent) — `/adjustments.html`

**Purpose:** Fix completion and calendar dates when something was marked on the wrong day.

**Workflow:** Student + around date → Refresh → pick a planned-day slot. Toggle **Full-day done** and/or assignment done independently; save a calendar date on a completed day. Un-completing a day unlocks planner edits. Student Today prefers an in-progress day or a day completed on today’s calendar date.

No screenshot in this folder yet.

---

### 8. Magic words (parent) — `/magic-words.html`

**Purpose:** Manage login passwords for self and students.

**Workflow:** Edit field → Save. Other parents’ words are read-only.

![Magic words](07-magic-words.png)

---

### 9. Theme (both) — `/theme.html`

**Purpose:** Device-local appearance (fonts, color presets, custom colors + live preview).

**Workflow:** Click a font or preset, or tweak custom colors; applies immediately and stores on this device. Same page; nav differs by role.

![Theme parent](08-theme.png)

![Theme student](10-theme-student-nav.png)

---

### 10. Today (student) — `/index.html`

**Purpose:** Student home for the current planned day slot.

**Workflows:**

- Required: Complete (with effort) or Request deferral; after complete, Update effort. Defer pending → “Waiting on parent”.
- Optional: expand Optional work → list or freeform + effort → Add & complete (hours await parent ack).

Empty state when no planned days: *“Ask a parent to plan work.”*

![Today](09-today-student.png)

---

## Audit notes from capture pass

- Catalog is populated (Math, Language, History, Science, Music) with seeded courses/assignments.
- Planned days exist for Evie, Hannah, and Judah; Ezra and Noah have none in the planner summary.
- Evie’s Today shows day slot #1 completed (Math Academy 30 XP + G&B Biology Lesson 1); Reports for Evie shows 1 full day / 1 hour in range.
- Requests badge = 1: pending optional hours for **Ezra — Nature walk** (no pending deferrals).
- Optional list is seeded (Free reading, Nature walk, Extra practice) plus “— new freeform —”.
- Parent Optional and student Today both create optional work that still needs Requests acknowledgment for hour credit.
- Theme offers kid fonts (Classic, Roundabout, Comic Book, Bubble Gum, Scribbles), color presets, custom colors, and a live preview panel.
- Adjustments and day-plan templates exist in the app; they were not part of this screenshot capture.
