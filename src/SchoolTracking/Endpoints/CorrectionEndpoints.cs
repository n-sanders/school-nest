using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Models;
using SchoolTracking.Services;

namespace SchoolTracking.Endpoints;

public static class CorrectionEndpoints
{
    public static RouteGroupBuilder MapCorrectionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/corrections");

        group.MapGet("/{studentId:int}/days", async (
            int studentId,
            AuthService auth,
            HttpContext http,
            AppDbContext db,
            DeferralService deferrals,
            string? around,
            int? dayId) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var student = await db.Users.FirstOrDefaultAsync(u =>
                u.Id == studentId && u.FamilyId == user.FamilyId && u.Role == UserRole.Student);
            if (student is null)
                return Results.NotFound(new { error = "Student not found" });

            await deferrals.MaybeRolloverStaleInProgressAsync(studentId);

            var aroundDate = DateOnly.FromDateTime(DateTime.Today);
            if (!string.IsNullOrWhiteSpace(around) && DateOnly.TryParse(around, out var parsedAround))
                aroundDate = parsedAround;

            var windowStart = aroundDate.AddDays(-14);
            var windowEnd = aroundDate.AddDays(14);

            var allDays = await db.PlannedDays
                .Where(d => d.StudentUserId == studentId)
                .Include(d => d.Assignments)
                    .ThenInclude(a => a.Course)
                        .ThenInclude(c => c!.Subject)
                .OrderBy(d => d.SequenceIndex)
                .ToListAsync();

            PlannedDay? selected = null;
            if (dayId is not null)
                selected = allDays.FirstOrDefault(d => d.Id == dayId.Value);

            if (selected is null)
            {
                selected = allDays.FirstOrDefault(d =>
                    PlannedDayStatuses.IsClosed(d.Status) && d.CalendarDate == aroundDate);
            }

            if (selected is null)
                selected = allDays.FirstOrDefault(d => d.Status == PlannedDayStatus.InProgress);

            if (selected is null)
            {
                selected = allDays
                    .Where(d => PlannedDayStatuses.IsClosed(d.Status) && d.CalendarDate is not null)
                    .OrderBy(d => Math.Abs(d.CalendarDate!.Value.DayNumber - aroundDate.DayNumber))
                    .ThenByDescending(d => d.SequenceIndex)
                    .FirstOrDefault();
            }

            if (selected is null)
                selected = allDays.FirstOrDefault(d => d.Status == PlannedDayStatus.Planned);

            var pickerDays = allDays
                .Where(d =>
                    d.Id == selected?.Id
                    || d.Status == PlannedDayStatus.InProgress
                    || (PlannedDayStatuses.IsClosed(d.Status)
                        && d.CalendarDate is not null
                        && d.CalendarDate >= windowStart
                        && d.CalendarDate <= windowEnd)
                    || (d.Status == PlannedDayStatus.Planned
                        && selected is not null
                        && Math.Abs(d.SequenceIndex - selected.SequenceIndex) <= 3)
                    || (d.Status == PlannedDayStatus.Planned && selected is null))
                .OrderByDescending(d => d.Status == PlannedDayStatus.InProgress)
                .ThenByDescending(d => d.CalendarDate ?? DateOnly.MinValue)
                .ThenBy(d => d.SequenceIndex)
                .Take(30)
                .ToList();

            // Ensure planned-only pickers stay readable when nothing is selected yet
            if (selected is null && pickerDays.Count == 0)
                pickerDays = allDays.Take(10).ToList();

            return Results.Ok(new
            {
                around = aroundDate.ToString("yyyy-MM-dd"),
                selectedDayId = selected?.Id,
                days = pickerDays.Select(ToDaySummary),
                day = selected is null ? null : ToDayDetail(selected)
            });
        });

        group.MapPatch("/days/{dayId:int}", async (
            int dayId,
            DayCorrectionRequest req,
            AuthService auth,
            HttpContext http,
            AppDbContext db,
            DeferralService deferrals) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var day = await db.PlannedDays
                .Include(d => d.Student)
                .Include(d => d.Assignments)
                    .ThenInclude(a => a.Course)
                        .ThenInclude(c => c!.Subject)
                .FirstOrDefaultAsync(d => d.Id == dayId && d.Student.FamilyId == user.FamilyId);
            if (day is null)
                return Results.NotFound(new { error = "Planned day not found" });

            DateOnly? requestedDate = null;
            var hasDateField = req.CalendarDate is not null;
            if (hasDateField)
            {
                if (string.IsNullOrWhiteSpace(req.CalendarDate))
                {
                    requestedDate = null;
                }
                else if (!DateOnly.TryParse(req.CalendarDate, out var parsed))
                {
                    return Results.BadRequest(new { error = "calendarDate must be yyyy-MM-dd" });
                }
                else
                {
                    requestedDate = parsed;
                }
            }

            if (req.Completed == true)
            {
                var calendarDate = requestedDate ?? day.CalendarDate;
                if (calendarDate is null)
                    return Results.BadRequest(new { error = "calendarDate is required when marking a day complete" });

                var duplicate = await ClosedDayOnDateAsync(db, day.StudentUserId, day.Id, calendarDate.Value);
                if (duplicate)
                    return Results.BadRequest(new { error = $"Another closed day already uses {calendarDate.Value:yyyy-MM-dd}" });

                await deferrals.SlideUnfinishedRequiredOffDayAsync(day);

                day.Status = PlannedDayStatus.Completed;
                day.CalendarDate = calendarDate;
                day.CompletedAt ??= DateTime.UtcNow;
            }
            else if (req.Completed == false)
            {
                if (!PlannedDayStatuses.IsClosed(day.Status))
                    return Results.BadRequest(new { error = "Day is not closed" });

                var otherInProgress = await db.PlannedDays.AnyAsync(d =>
                    d.StudentUserId == day.StudentUserId
                    && d.Id != day.Id
                    && d.Status == PlannedDayStatus.InProgress);
                if (otherInProgress)
                {
                    return Results.BadRequest(new
                    {
                        error = "Another day is already in progress. Finish or adjust that day first so the student only has one active day."
                    });
                }

                day.Status = PlannedDayStatus.InProgress;
                day.CalendarDate = null;
                day.CompletedAt = null;
            }
            else if (hasDateField)
            {
                if (!PlannedDayStatuses.IsClosed(day.Status))
                    return Results.BadRequest(new { error = "Only closed days have a calendar date" });
                if (requestedDate is null)
                    return Results.BadRequest(new { error = "calendarDate cannot be cleared while the day is closed" });

                var duplicate = await ClosedDayOnDateAsync(db, day.StudentUserId, day.Id, requestedDate.Value);
                if (duplicate)
                    return Results.BadRequest(new { error = $"Another closed day already uses {requestedDate:yyyy-MM-dd}" });

                day.CalendarDate = requestedDate;
            }
            else
            {
                return Results.BadRequest(new { error = "Provide completed and/or calendarDate" });
            }

            await db.SaveChangesAsync();

            var fresh = await db.PlannedDays
                .Include(d => d.Assignments)
                    .ThenInclude(a => a.Course)
                        .ThenInclude(c => c!.Subject)
                .FirstAsync(d => d.Id == day.Id);
            return Results.Ok(ToDayDetail(fresh));
        });

        group.MapPatch("/assignments/{id:int}", async (
            int id,
            AssignmentCorrectionRequest req,
            AuthService auth,
            HttpContext http,
            AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var assignment = await db.Assignments
                .Include(a => a.Student)
                .Include(a => a.Course).ThenInclude(c => c!.Subject)
                .Include(a => a.PlannedDay)
                .FirstOrDefaultAsync(a => a.Id == id && a.Student.FamilyId == user.FamilyId);
            if (assignment is null)
                return Results.NotFound(new { error = "Assignment not found" });

            var hasDateField = req.ActivityDate is not null;
            DateOnly? requestedDate = null;
            if (hasDateField)
            {
                if (string.IsNullOrWhiteSpace(req.ActivityDate))
                {
                    requestedDate = null;
                }
                else if (!DateOnly.TryParse(req.ActivityDate, out var parsed))
                {
                    return Results.BadRequest(new { error = "activityDate must be yyyy-MM-dd" });
                }
                else
                {
                    requestedDate = parsed;
                }
            }

            if (req.Completed == true)
            {
                if (assignment.Status is AssignmentStatus.Deferred or AssignmentStatus.DeferRequested)
                    return Results.BadRequest(new { error = "Cannot mark deferred assignments complete here" });

                assignment.Status = AssignmentStatus.Completed;
                assignment.CompletedAt ??= DateTime.UtcNow;
                if (hasDateField)
                    assignment.ActivityDate = requestedDate;
                else
                    assignment.ActivityDate ??= DateOnly.FromDateTime(DateTime.Today);
            }
            else if (req.Completed == false)
            {
                if (assignment.Status != AssignmentStatus.Completed)
                    return Results.BadRequest(new { error = "Only completed assignments can be marked not done" });

                assignment.Status = AssignmentStatus.Assigned;
                assignment.CompletedAt = null;
                if (hasDateField)
                    assignment.ActivityDate = requestedDate;
            }
            else if (hasDateField)
            {
                if (assignment.Status != AssignmentStatus.Completed)
                    return Results.BadRequest(new { error = "Only completed assignments use activity dates for reports" });
                assignment.ActivityDate = requestedDate;
            }
            else
            {
                return Results.BadRequest(new { error = "Provide completed and/or activityDate" });
            }

            await db.SaveChangesAsync();
            return Results.Ok(AssignmentHelpers.ToDto(
                assignment, assignment.Course, assignment.Course?.Subject, assignment.PlannedDay));
        });

        return group;
    }

    private static Task<bool> ClosedDayOnDateAsync(AppDbContext db, int studentUserId, int exceptDayId, DateOnly calendarDate) =>
        db.PlannedDays.AnyAsync(d =>
            d.StudentUserId == studentUserId
            && d.Id != exceptDayId
            && (d.Status == PlannedDayStatus.Completed || d.Status == PlannedDayStatus.PartiallyCompleted)
            && d.CalendarDate == calendarDate);

    private static object ToDaySummary(PlannedDay d)
    {
        var required = d.Assignments.Where(a => a.Kind == AssignmentKind.Required).ToList();
        return new
        {
            d.Id,
            d.SequenceIndex,
            status = d.Status.ToString().ToLowerInvariant(),
            calendarDate = d.CalendarDate?.ToString("yyyy-MM-dd"),
            completedAt = d.CompletedAt,
            assignmentCount = required.Count,
            completedCount = required.Count(a => a.Status == AssignmentStatus.Completed)
        };
    }

    private static object ToDayDetail(PlannedDay d) => new
    {
        d.Id,
        d.StudentUserId,
        d.SequenceIndex,
        status = d.Status.ToString().ToLowerInvariant(),
        calendarDate = d.CalendarDate?.ToString("yyyy-MM-dd"),
        completedAt = d.CompletedAt,
        assignments = d.Assignments
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.Course?.Subject?.Name)
            .ThenBy(a => a.Name)
            .Select(a => AssignmentHelpers.ToDto(a, a.Course, a.Course?.Subject, d))
            .ToList()
    };

    public record DayCorrectionRequest(bool? Completed, string? CalendarDate);
    public record AssignmentCorrectionRequest(bool? Completed, string? ActivityDate);
}
