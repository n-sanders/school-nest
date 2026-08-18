using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Models;
using SchoolTracking.Services;

namespace SchoolTracking.Endpoints;

public static class PlannerEndpoints
{
    public static RouteGroupBuilder MapPlannerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/planner");

        group.MapGet("/{studentId:int}/days", async (int studentId, AuthService auth, HttpContext http, AppDbContext db, DeferralService deferrals) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var student = await db.Users.FirstOrDefaultAsync(u =>
                u.Id == studentId && u.FamilyId == user.FamilyId && u.Role == UserRole.Student);
            if (student is null)
                return Results.NotFound(new { error = "Student not found" });

            await deferrals.MaybeRolloverStaleInProgressAsync(studentId);

            var days = await db.PlannedDays
                .Where(d => d.StudentUserId == studentId)
                .OrderBy(d => d.SequenceIndex)
                .Include(d => d.Assignments)
                .ThenInclude(a => a.Course)
                .ThenInclude(c => c.Subject)
                .ToListAsync();

            return Results.Ok(days.Select(d => new
            {
                d.Id,
                d.SequenceIndex,
                status = d.Status.ToString().ToLowerInvariant(),
                calendarDate = d.CalendarDate?.ToString("yyyy-MM-dd"),
                assignments = d.Assignments
                    .OrderBy(a => a.Course.Subject.SortOrder)
                    .ThenBy(a => a.Course.SortOrder)
                    .Select(a => AssignmentHelpers.ToDto(a, a.Course, a.Course.Subject, d))
            }));
        });

        group.MapPost("/{studentId:int}/days", async (int studentId, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var student = await db.Users.FirstOrDefaultAsync(u =>
                u.Id == studentId && u.FamilyId == user.FamilyId && u.Role == UserRole.Student);
            if (student is null)
                return Results.NotFound(new { error = "Student not found" });

            var max = await db.PlannedDays.Where(d => d.StudentUserId == studentId)
                .MaxAsync(d => (int?)d.SequenceIndex) ?? 0;

            var day = new PlannedDay
            {
                StudentUserId = studentId,
                SequenceIndex = max + 1,
                Status = PlannedDayStatus.Planned
            };
            db.PlannedDays.Add(day);
            await db.SaveChangesAsync();
            return Results.Ok(new { day.Id, day.SequenceIndex, status = "planned", assignments = Array.Empty<object>() });
        });

        group.MapPost("/{studentId:int}/days/{dayId:int}/assignments", async (
            int studentId, int dayId, AssignFromCatalogRequest req,
            AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var student = await db.Users.FirstOrDefaultAsync(u =>
                u.Id == studentId && u.FamilyId == user.FamilyId && u.Role == UserRole.Student);
            if (student is null)
                return Results.NotFound(new { error = "Student not found" });

            var day = await db.PlannedDays.FirstOrDefaultAsync(d =>
                d.Id == dayId && d.StudentUserId == studentId);
            if (day is null)
                return Results.NotFound(new { error = "Planned day not found" });
            if (PlannedDayStatuses.IsClosed(day.Status))
                return Results.BadRequest(new { error = "Cannot edit a closed day" });

            var catalog = await db.CatalogAssignments
                .Include(a => a.Course).ThenInclude(c => c.Subject)
                .FirstOrDefaultAsync(a => a.Id == req.CatalogAssignmentId && a.Course.Subject.FamilyId == user.FamilyId);
            if (catalog is null)
                return Results.NotFound(new { error = "Catalog assignment not found" });

            var existsForCourse = await db.Assignments.AnyAsync(a =>
                a.PlannedDayId == dayId
                && a.CourseId == catalog.CourseId
                && a.Kind == AssignmentKind.Required
                && a.Status != AssignmentStatus.Deferred);
            if (existsForCourse)
                return Results.BadRequest(new { error = "This day already has a required assignment for that course" });

            EffortLevel effort = catalog.DefaultEffort;
            if (req.Effort is not null)
            {
                if (!AssignmentHelpers.TryParseEffort(req.Effort, out effort))
                    return Results.BadRequest(new { error = "effort must be low or high" });
            }

            var assignment = new Assignment
            {
                StudentUserId = studentId,
                CourseId = catalog.CourseId,
                CatalogAssignmentId = catalog.Id,
                PlannedDayId = dayId,
                Name = catalog.Name,
                Url = catalog.Url,
                Description = catalog.Description,
                Effort = effort,
                Kind = AssignmentKind.Required,
                Status = AssignmentStatus.Assigned
            };
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            return Results.Ok(AssignmentHelpers.ToDto(assignment, catalog.Course, catalog.Course.Subject, day));
        });

        group.MapDelete("/assignments/{assignmentId:int}", async (int assignmentId, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var assignment = await db.Assignments
                .Include(a => a.Student)
                .Include(a => a.PlannedDay)
                .FirstOrDefaultAsync(a => a.Id == assignmentId && a.Student.FamilyId == user.FamilyId);
            if (assignment is null)
                return Results.NotFound();
            if (assignment.PlannedDay is not null && PlannedDayStatuses.IsClosed(assignment.PlannedDay.Status))
                return Results.BadRequest(new { error = "Cannot remove from a closed day" });

            db.Assignments.Remove(assignment);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        return group;
    }

    public record AssignFromCatalogRequest(int CatalogAssignmentId, string? Effort);
}
