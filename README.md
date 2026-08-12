# School Nest

Self-hosted homeschool activity tracker (repo: SchoolTracking). Parents plan ordered day slots, students complete or defer work, optional hours need parent acknowledgment, and compliance reports track estimated hours (Low = 30m, High = 60m).

Meant for a trusted home network or reverse proxy — not public-internet auth.

## Stack

- ASP.NET Core 9 Minimal APIs
- SQLite (single file)
- Static HTML + vanilla JavaScript (served by the same app)
- Docker Compose with a named volume for the database

## Run with Docker

Compose publishes **host 5292 → container 8080**, sets `TZ=America/Chicago` so “today” matches household calendar dates, and joins the external Docker network `web-core_default` (homelab reverse proxy). Create that network first if it does not exist:

```bash
docker network create web-core_default
docker compose up --build
```

Open http://localhost:5292/login.html

If you previously fixed evening date-rollover by changing the **host** timezone, compose `TZ` is enough; the host workaround is not required.

### Seed logins

| Person | Magic word |
|--------|------------|
| Mama | `kate` |
| Papa | `nate` |
| Evie | `bearcat` |
| Noah | `spacex` |
| Hannah | `tater` |
| Judah | `minecraft` |
| Ezra | `cat` |

Parents can change magic words under **Magic words**.

### Backup

Copy the SQLite file from the Docker volume (path inside the container: `/data/school.db`).

```bash
docker compose cp schooltracking:/data/school.db ./school-backup.db
```

## Run locally (without Docker)

```bash
dotnet run --project src/SchoolTracking
```

That uses launchSettings: http://localhost:5292. Override with `--urls` if needed.

The database defaults to `src/SchoolTracking/storage/school.db`. Override with config/env `Database__Path`.

To re-seed from scratch, delete the SQLite file and restart the app.

## Main flows

- **Parent:** Catalog → Planner → Requests (deferrals + optional-hour ack) → Optional (log for a kid) → Reports → Adjustments → Magic words → Theme
- **Student:** Today (complete / request deferral / log optional) → Theme (Layout switcher on Today)

Course work is assigned only by parents via the planner. Optional activities use a separate family list (not subjects/courses); freeform optional names are added to that list.

**Effort:** set when assigning. Students can change it on Today (on complete, and after). Parents can change it on Requests for pending optional hours. There is no general parent UI for past required-work effort.

**Day-plan templates** (Planner): per-kid shortcuts stored in that browser’s localStorage — not shared across parents or devices.

`/deferrals.html` redirects to Requests.

Page-by-page walkthrough with screenshots: [audit-screenshots/README.md](audit-screenshots/README.md). Later architecture notes: [docs/follow-ups.md](docs/follow-ups.md).
