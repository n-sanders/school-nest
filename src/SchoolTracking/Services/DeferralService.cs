using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Models;

namespace SchoolTracking.Services;

public class DeferralService(AppDbContext db)
{
    /// <summary>
    /// Approve deferral: move assignment to next planned day for same course,
    /// shifting that course's later required items one slot forward.
    /// </summary>
    public async Task<(bool ok, string? error)> ApproveDeferralAsync(Assignment assignment)
    {
        if (assignment.Kind != AssignmentKind.Required)
            return (false, "Only required assignments can be deferred");
        if (assignment.Status != AssignmentStatus.DeferRequested)
            return (false, "Assignment is not awaiting deferral");
        if (assignment.PlannedDayId is null)
            return (false, "Assignment is not on a planned day");

        var currentDay = await db.PlannedDays
            .FirstOrDefaultAsync(d => d.Id == assignment.PlannedDayId.Value);
        if (currentDay is null)
            return (false, "Planned day not found");

        if (currentDay.Status == PlannedDayStatus.Planned)
            currentDay.Status = PlannedDayStatus.InProgress;

        var laterDays = await db.PlannedDays
            .Where(d => d.StudentUserId == assignment.StudentUserId
                        && d.SequenceIndex > currentDay.SequenceIndex
                        && d.Status != PlannedDayStatus.Completed)
            .OrderBy(d => d.SequenceIndex)
            .ToListAsync();

        var laterDayIds = laterDays.Select(d => d.Id).ToList();
        var laterCourseAssignments = await db.Assignments
            .Where(a => a.StudentUserId == assignment.StudentUserId
                        && a.CourseId == assignment.CourseId
                        && a.Kind == AssignmentKind.Required
                        && a.PlannedDayId != null
                        && laterDayIds.Contains(a.PlannedDayId.Value)
                        && a.Status != AssignmentStatus.Completed
                        && a.Status != AssignmentStatus.Deferred
                        && a.Id != assignment.Id)
            .ToListAsync();

        var byDay = laterCourseAssignments
            .GroupBy(a => a.PlannedDayId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var chain = new List<Assignment> { assignment };
        foreach (var day in laterDays)
        {
            if (byDay.TryGetValue(day.Id, out var next))
                chain.Add(next);
            else
                break;
        }

        while (laterDays.Count < chain.Count)
        {
            var maxSeq = await db.PlannedDays
                .Where(d => d.StudentUserId == assignment.StudentUserId)
                .Select(d => (int?)d.SequenceIndex)
                .MaxAsync() ?? currentDay.SequenceIndex;

            var newDay = new PlannedDay
            {
                StudentUserId = assignment.StudentUserId,
                SequenceIndex = maxSeq + 1,
                Status = PlannedDayStatus.Planned
            };
            db.PlannedDays.Add(newDay);
            await db.SaveChangesAsync();
            laterDays.Add(newDay);
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            chain[i].PlannedDayId = laterDays[i].Id;
            chain[i].Status = AssignmentStatus.Assigned;
            chain[i].ActivityDate = null;
        }

        // Persist slides first so day-completion queries see the updated PlannedDayId values.
        await db.SaveChangesAsync();
        await MaybeCompleteDayAsync(currentDay);
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error)> RejectDeferralAsync(Assignment assignment)
    {
        if (assignment.Status != AssignmentStatus.DeferRequested)
            return (false, "Assignment is not awaiting deferral");

        assignment.Status = AssignmentStatus.Assigned;
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task MaybeCompleteDayAsync(PlannedDay day)
    {
        if (day.Status == PlannedDayStatus.Completed)
            return;

        var required = await db.Assignments
            .Where(a => a.PlannedDayId == day.Id && a.Kind == AssignmentKind.Required)
            .ToListAsync();

        // Empty after all required items slid away via deferral — complete if work had started.
        if (required.Count == 0)
        {
            if (day.Status != PlannedDayStatus.InProgress)
                return;
        }
        else if (!required.All(a =>
                     a.Status is AssignmentStatus.Completed or AssignmentStatus.Deferred))
        {
            return;
        }

        day.Status = PlannedDayStatus.Completed;
        day.CalendarDate ??= DateOnly.FromDateTime(DateTime.Today);
        day.CompletedAt = DateTime.UtcNow;

        foreach (var a in required.Where(a => a.Status == AssignmentStatus.Completed && a.ActivityDate is null))
            a.ActivityDate = day.CalendarDate;
    }
}
