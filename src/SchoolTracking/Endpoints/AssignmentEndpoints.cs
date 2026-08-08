using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Models;
using SchoolTracking.Services;

namespace SchoolTracking.Endpoints;

public static class AssignmentEndpoints
{
    public static RouteGroupBuilder MapAssignmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/assignments");

        group.MapGet("/today", async (AuthService auth, HttpContext http, AppDbContext db, DeferralService deferrals) =>
        {
            var user = await http.RequireUserAsync(auth);
            if (user is null) return Results.Empty;
            if (!user.IsStudent)
                return Results.BadRequest(new { error = "Students only" });

            var day = await GetOrActivateCurrentDayAsync(db, user.Id);
            if (day is null)
                return Results.Ok(new { day = (object?)null, assignments = Array.Empty<object>(), message = "No planned days yet. Ask a parent to plan work." });

            await db.Entry(day).Collection(d => d.Assignments).Query()
                .Include(a => a.Course).ThenInclude(c => c.Subject)
                .LoadAsync();

            var required = day.Assignments
                .Where(a => a.Kind == AssignmentKind.Required)
                .OrderBy(a => a.Course!.Subject.SortOrder)
                .ThenBy(a => a.Course!.SortOrder)
                .Select(a => AssignmentHelpers.ToDto(a, a.Course, a.Course!.Subject, day))
                .ToList();

            var today = DateOnly.FromDateTime(DateTime.Today);
            var optionalToday = await db.Assignments
                .Include(a => a.Course).ThenInclude(c => c.Subject)
                .Where(a => a.StudentUserId == user.Id && a.Kind == AssignmentKind.Optional && a.ActivityDate == today)
                .OrderByDescending(a => a.Id)
                .ToListAsync();

            return Results.Ok(new
            {
                day = new
                {
                    day.Id,
                    day.SequenceIndex,
                    status = day.Status.ToString().ToLowerInvariant(),
                    calendarDate = day.CalendarDate?.ToString("yyyy-MM-dd") ?? today.ToString("yyyy-MM-dd")
                },
                assignments = required,
                optional = optionalToday.Select(a => AssignmentHelpers.ToDto(a, a.Course, a.Course?.Subject, day))
            });
        });

        group.MapPost("/{id:int}/complete", async (int id, CompleteRequest req, AuthService auth, HttpContext http, AppDbContext db, DeferralService deferrals) =>
        {
            var user = await http.RequireUserAsync(auth);
            if (user is null) return Results.Empty;

            var assignment = await LoadFamilyAssignmentAsync(db, id, user.FamilyId);
            if (assignment is null)
                return Results.NotFound();

            if (user.IsStudent && assignment.StudentUserId != user.Id)
                return Results.Forbid();

            if (assignment.Status is AssignmentStatus.Completed or AssignmentStatus.Deferred)
                return Results.BadRequest(new { error = "Already completed or deferred" });

            if (req.Effort is not null)
            {
                if (!AssignmentHelpers.TryParseEffort(req.Effort, out var effort))
                    return Results.BadRequest(new { error = "effort must be low or high" });
                assignment.Effort = effort;
            }

            assignment.Status = AssignmentStatus.Completed;
            assignment.CompletedAt = DateTime.UtcNow;
            assignment.ActivityDate ??= DateOnly.FromDateTime(DateTime.Today);

            if (assignment.Kind == AssignmentKind.Required && assignment.PlannedDayId is not null)
            {
                var day = await db.PlannedDays.FirstAsync(d => d.Id == assignment.PlannedDayId.Value);
                if (day.Status == PlannedDayStatus.Planned)
                    day.Status = PlannedDayStatus.InProgress;
                await deferrals.MaybeCompleteDayAsync(day);
            }

            await db.SaveChangesAsync();
            return Results.Ok(AssignmentHelpers.ToDto(assignment, assignment.Course, assignment.Course?.Subject, assignment.PlannedDay));
        });

        group.MapPost("/{id:int}/defer", async (int id, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireUserAsync(auth);
            if (user is null) return Results.Empty;
            if (!user.IsStudent)
                return Results.BadRequest(new { error = "Students request deferrals" });

            var assignment = await LoadFamilyAssignmentAsync(db, id, user.FamilyId);
            if (assignment is null)
                return Results.NotFound();
            if (assignment.StudentUserId != user.Id)
                return Results.Forbid();
            if (assignment.Kind != AssignmentKind.Required)
                return Results.BadRequest(new { error = "Only required assignments can be deferred" });
            if (assignment.Status != AssignmentStatus.Assigned)
                return Results.BadRequest(new { error = "Only assigned work can request deferral" });

            assignment.Status = AssignmentStatus.DeferRequested;
            await db.SaveChangesAsync();
            return Results.Ok(AssignmentHelpers.ToDto(assignment, assignment.Course, assignment.Course?.Subject, assignment.PlannedDay));
        });

        group.MapGet("/deferrals", async (AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var items = await db.Assignments
                .Include(a => a.Student)
                .Include(a => a.Course).ThenInclude(c => c.Subject)
                .Include(a => a.PlannedDay)
                .Where(a => a.Student.FamilyId == user.FamilyId && a.Status == AssignmentStatus.DeferRequested)
                .OrderBy(a => a.Student.DisplayName)
                .ThenBy(a => a.Id)
                .ToListAsync();

            return Results.Ok(items.Select(a => new
            {
                assignment = AssignmentHelpers.ToDto(a, a.Course, a.Course?.Subject, a.PlannedDay),
                studentName = a.Student.DisplayName
            }));
        });

        group.MapPost("/{id:int}/defer/approve", async (int id, AuthService auth, HttpContext http, AppDbContext db, DeferralService deferrals) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var assignment = await LoadFamilyAssignmentAsync(db, id, user.FamilyId);
            if (assignment is null)
                return Results.NotFound();

            var (ok, error) = await deferrals.ApproveDeferralAsync(assignment);
            if (!ok)
                return Results.BadRequest(new { error });

            await db.Entry(assignment).ReloadAsync();
            await db.Entry(assignment).Reference(a => a.Course).LoadAsync();
            if (assignment.Course is not null)
                await db.Entry(assignment.Course).Reference(c => c.Subject).LoadAsync();
            await db.Entry(assignment).Reference(a => a.PlannedDay).LoadAsync();

            return Results.Ok(AssignmentHelpers.ToDto(assignment, assignment.Course, assignment.Course?.Subject, assignment.PlannedDay));
        });

        group.MapPost("/{id:int}/defer/reject", async (int id, AuthService auth, HttpContext http, AppDbContext db, DeferralService deferrals) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var assignment = await LoadFamilyAssignmentAsync(db, id, user.FamilyId);
            if (assignment is null)
                return Results.NotFound();

            var (ok, error) = await deferrals.RejectDeferralAsync(assignment);
            if (!ok)
                return Results.BadRequest(new { error });

            return Results.Ok(AssignmentHelpers.ToDto(assignment, assignment.Course, assignment.Course?.Subject, assignment.PlannedDay));
        });

        group.MapPatch("/{id:int}/effort", async (int id, EffortRequest req, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireUserAsync(auth);
            if (user is null) return Results.Empty;

            var assignment = await LoadFamilyAssignmentAsync(db, id, user.FamilyId);
            if (assignment is null)
                return Results.NotFound();

            if (user.IsStudent && assignment.StudentUserId != user.Id)
                return Results.Forbid();

            if (!AssignmentHelpers.TryParseEffort(req.Effort, out var effort))
                return Results.BadRequest(new { error = "effort must be low or high" });

            assignment.Effort = effort;
            await db.SaveChangesAsync();
            return Results.Ok(AssignmentHelpers.ToDto(assignment, assignment.Course, assignment.Course?.Subject, assignment.PlannedDay));
        });

        group.MapPost("/optional", async (OptionalRequest req, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireUserAsync(auth);
            if (user is null) return Results.Empty;

            int studentId;
            if (user.IsStudent)
            {
                studentId = user.Id;
            }
            else
            {
                if (req.StudentId is null)
                    return Results.BadRequest(new { error = "studentId required for parents" });
                var student = await db.Users.FirstOrDefaultAsync(u =>
                    u.Id == req.StudentId && u.FamilyId == user.FamilyId && u.Role == UserRole.Student);
                if (student is null)
                    return Results.NotFound(new { error = "Student not found" });
                studentId = student.Id;
            }

            DateOnly activityDate;
            if (!string.IsNullOrWhiteSpace(req.ActivityDate))
            {
                if (!DateOnly.TryParse(req.ActivityDate, out activityDate))
                    return Results.BadRequest(new { error = "activityDate must be yyyy-MM-dd" });
            }
            else
            {
                activityDate = DateOnly.FromDateTime(DateTime.Today);
            }

            OptionalActivity? activity = null;
            string name;
            string? url = null;
            string? description = null;
            EffortLevel effort = EffortLevel.Low;

            if (req.OptionalActivityId is not null)
            {
                activity = await db.OptionalActivities
                    .FirstOrDefaultAsync(a => a.Id == req.OptionalActivityId && a.FamilyId == user.FamilyId);
                if (activity is null)
                    return Results.NotFound(new { error = "Optional activity not found" });
                name = activity.Name;
                url = activity.Url;
                description = activity.Description;
                effort = activity.DefaultEffort;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(req.Name))
                    return Results.BadRequest(new { error = "Freeform optional requires a name" });

                name = req.Name.Trim();
                url = string.IsNullOrWhiteSpace(req.Url) ? null : req.Url.Trim();
                description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
                if (req.Effort is not null && AssignmentHelpers.TryParseEffort(req.Effort, out var e0))
                    effort = e0;

                var max = await db.OptionalActivities.Where(a => a.FamilyId == user.FamilyId)
                    .MaxAsync(a => (int?)a.SortOrder) ?? 0;
                activity = new OptionalActivity
                {
                    FamilyId = user.FamilyId,
                    Name = name,
                    Url = url,
                    Description = description,
                    DefaultEffort = effort,
                    SortOrder = max + 1
                };
                db.OptionalActivities.Add(activity);
                await db.SaveChangesAsync();
            }

            if (req.Effort is not null)
            {
                if (!AssignmentHelpers.TryParseEffort(req.Effort, out effort))
                    return Results.BadRequest(new { error = "effort must be low or high" });
            }

            var assignment = new Assignment
            {
                StudentUserId = studentId,
                CourseId = null,
                CatalogAssignmentId = null,
                OptionalActivityId = activity.Id,
                PlannedDayId = null,
                Name = name,
                Url = url,
                Description = description,
                Effort = effort,
                Kind = AssignmentKind.Optional,
                Status = req.CompleteImmediately == true ? AssignmentStatus.Completed : AssignmentStatus.Assigned,
                CompletedAt = req.CompleteImmediately == true ? DateTime.UtcNow : null,
                ActivityDate = activityDate,
                HoursAcknowledgedAt = null
            };
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            return Results.Ok(AssignmentHelpers.ToDto(assignment));
        });

        group.MapGet("/optional/pending", async (AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var items = await db.Assignments
                .Include(a => a.Student)
                .Include(a => a.Course).ThenInclude(c => c.Subject)
                .Where(a => a.Student.FamilyId == user.FamilyId
                            && a.Kind == AssignmentKind.Optional
                            && a.Status == AssignmentStatus.Completed
                            && a.HoursAcknowledgedAt == null)
                .OrderBy(a => a.ActivityDate)
                .ThenBy(a => a.Id)
                .ToListAsync();

            return Results.Ok(items.Select(a => new
            {
                assignment = AssignmentHelpers.ToDto(a, a.Course, a.Course?.Subject),
                studentName = a.Student.DisplayName
            }));
        });

        group.MapPost("/{id:int}/acknowledge", async (int id, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var assignment = await LoadFamilyAssignmentAsync(db, id, user.FamilyId);
            if (assignment is null)
                return Results.NotFound();
            if (assignment.Kind != AssignmentKind.Optional)
                return Results.BadRequest(new { error = "Only optional work needs acknowledgment" });
            if (assignment.Status != AssignmentStatus.Completed)
                return Results.BadRequest(new { error = "Assignment must be completed first" });

            assignment.HoursAcknowledgedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(AssignmentHelpers.ToDto(assignment, assignment.Course, assignment.Course?.Subject));
        });

        // Parent view of a student's recent assignments for effort override
        group.MapGet("/student/{studentId:int}", async (int studentId, AuthService auth, HttpContext http, AppDbContext db, int? days) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var student = await db.Users.FirstOrDefaultAsync(u =>
                u.Id == studentId && u.FamilyId == user.FamilyId && u.Role == UserRole.Student);
            if (student is null)
                return Results.NotFound();

            var takeDays = days ?? 60;
            var since = DateOnly.FromDateTime(DateTime.Today.AddDays(-takeDays));

            var items = await db.Assignments
                .Include(a => a.Course).ThenInclude(c => c.Subject)
                .Include(a => a.PlannedDay)
                .Where(a => a.StudentUserId == studentId &&
                            (a.ActivityDate == null || a.ActivityDate >= since || a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.DeferRequested))
                .OrderByDescending(a => a.ActivityDate)
                .ThenByDescending(a => a.Id)
                .Take(200)
                .ToListAsync();

            return Results.Ok(items.Select(a => AssignmentHelpers.ToDto(a, a.Course, a.Course?.Subject, a.PlannedDay)));
        });

        return group;
    }

    private static async Task<Assignment?> LoadFamilyAssignmentAsync(AppDbContext db, int id, int familyId)
    {
        return await db.Assignments
            .Include(a => a.Student)
            .Include(a => a.Course).ThenInclude(c => c.Subject)
            .Include(a => a.PlannedDay)
            .FirstOrDefaultAsync(a => a.Id == id && a.Student.FamilyId == familyId);
    }

    private static async Task<PlannedDay?> GetOrActivateCurrentDayAsync(AppDbContext db, int studentId)
    {
        var day = await db.PlannedDays
            .Where(d => d.StudentUserId == studentId && d.Status != PlannedDayStatus.Completed)
            .OrderBy(d => d.SequenceIndex)
            .FirstOrDefaultAsync();

        if (day is null)
            return null;

        if (day.Status == PlannedDayStatus.Planned)
        {
            day.Status = PlannedDayStatus.InProgress;
            await db.SaveChangesAsync();
        }

        return day;
    }

    public record CompleteRequest(string? Effort);
    public record EffortRequest(string Effort);
    public record OptionalRequest(
        int? StudentId,
        int? OptionalActivityId,
        string? Name,
        string? Url,
        string? Description,
        string? Effort,
        string? ActivityDate,
        bool? CompleteImmediately);
}
