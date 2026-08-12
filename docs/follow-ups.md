# Architecture follow-ups

Notes from an Aug 2026 review. Not blocking daily use on a trusted home network. Do not treat this as a commitment to implement every item.

## Schema / migrations

[`SeedData.cs`](../src/SchoolTracking/Data/SeedData.cs) uses `EnsureCreated` and **deletes the whole database** if the `OptionalActivities` table is missing. EF Design is referenced but there are no migrations. The next column or table change has no safe path; the wipe-if-missing trick will not add columns to an existing file.

**Later:** add EF migrations (or stop deleting) before the next schema change.

## `AssignmentStatus.Deferred` is unused

Approve-deferral **slides** the item to a later day and sets status back to `Assigned` ([`DeferralService.cs`](../src/SchoolTracking/Services/DeferralService.cs)). Nothing ever writes `Deferred`. The enum, DTO, planner filters, and day-completion checks still treat it as live — leftover from an earlier “leave a deferred marker on the original day” design.

**Later:** remove the unused status, or persist `Deferred` on the vacated slot if that history is wanted.

## Day-plan templates are device-local

Templates live in `localStorage` (`schooltracking-planner-template` in [`planner.js`](../src/SchoolTracking/wwwroot/js/planner.js)). Parents do not share plans; a new browser loses them.

Fine as a personal shortcut (documented in the README). Out of line if they were meant to be family data — then store them on the server.

## Planner cannot delete days; only 5 open slots show

[`PlannerEndpoints`](../src/SchoolTracking/Endpoints/PlannerEndpoints.cs) can add days and remove assignments, not delete a `PlannedDay`. Empty days (including extras created by deferral) stay forever. The UI only shows 5 uncompleted days (`SLOTS = 5` in planner.js), so a long queue hides the rest and blocks “Add day”.

**Later:** delete (or archive) empty/unwanted days; raise or remove the slot cap.

## Catalog is add-mostly

Subjects and courses cannot be renamed or deleted. Only catalog assignments can be edited/deleted, and delete is blocked if any student assignment references them.

Fine for MVP; parents will hit it when a course is retired.

## Reports “remaining” vs selected range

`hoursRemaining = yearlyTarget - hoursInFilter` ([`ReportEndpoints.cs`](../src/SchoolTracking/Endpoints/ReportEndpoints.cs)). Default filter is ~12 months, so it looks right. A week filter makes “remaining” meaningless.

**Later:** label remaining vs target **in this range**, or always compute remaining against a school-year window.

## Auth is family-LAN appropriate, not internet-appropriate

Plaintext magic words, unauthenticated `GET /api/auth/users` (needed for the login picker), 30-day HttpOnly cookie without `Secure`, no HTTPS in compose.

Acceptable behind a trusted network / reverse proxy (stated in the README). Do not imply it is safe on the public internet without a real auth/HTTPS pass.

## Smaller leftovers

- Unused `GET /api/assignments/student/{id}` — likely the missing parent effort-history UI. Either build that UI or drop the endpoint.
- Students can complete any of their assignments by id, not only the current day.
- Correction “uncomplete day” clears `CalendarDate` (easy to lose the original date).
- Login title **SchoolNest** vs nav **School Nest**.
- No automated tests.

## Already done (not follow-ups)

- Compose `TZ=America/Chicago` so `DateTime.Today` in the container matches household calendar dates.
- README and audit walkthrough updated to match current nav, ports, and effort rules.
