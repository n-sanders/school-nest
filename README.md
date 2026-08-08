# SchoolTracking

Self-hosted homeschool activity tracker: parents plan ordered day slots, students complete or defer work, optional hours need parent acknowledgment, and compliance reports track estimated hours (low = 30m, high = 60m).

## Stack

- ASP.NET Core 9 Minimal APIs
- SQLite (single file)
- Static HTML + vanilla JavaScript (served by the same app)
- Docker Compose with a named volume for the database

## Run with Docker

```bash
docker compose up --build
```

Open http://localhost:8080/login.html

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

Copy the SQLite file from the Docker volume (default path inside the container: `/data/school.db`).

```bash
docker compose cp schooltracking:/data/school.db ./school-backup.db
```

## Run locally (without Docker)

```bash
dotnet run --project src/SchoolTracking --urls http://127.0.0.1:8080
```

The database defaults to `src/SchoolTracking/storage/school.db`. Override with config/env `Database__Path`.

To re-seed from scratch, delete the SQLite file and restart the app.

## Main flows

- **Parent:** Catalog → Planner → Deferrals → Optional ack → Reports → Magic words
- **Student:** Today (complete / request deferral / log optional)

Course work is assigned only by parents via the planner. Optional activities use a separate family list (not subjects/courses); freeform optional names are added to that list.

Effort is set when assigning, adjustable by the student on complete, and overridable by parents anytime (including past days).
