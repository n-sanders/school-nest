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

        await SlideAssignmentForwardAsync(assignment, currentDay);
        assignment.Status = AssignmentStatus.Assigned;
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
        if (PlannedDayStatuses.IsClosed(day.Status))
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

    /// <summary>
    /// If the in-progress day has leftover required work and at least one required
    /// completion on a prior calendar date, slide leftovers forward and close the
    /// slot as partially completed. Doing nothing on a day does not advance the queue.
    /// </summary>
    public async Task MaybeRolloverStaleInProgressAsync(int studentId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var inProgress = await db.PlannedDays
            .Where(d => d.StudentUserId == studentId && d.Status == PlannedDayStatus.InProgress)
            .OrderBy(d => d.SequenceIndex)
            .FirstOrDefaultAsync();
        if (inProgress is null)
            return;

        var required = await db.Assignments
            .Where(a => a.PlannedDayId == inProgress.Id && a.Kind == AssignmentKind.Required)
            .ToListAsync();

        var leftovers = required
            .Where(a => a.Status is AssignmentStatus.Assigned or AssignmentStatus.DeferRequested)
            .ToList();
        if (leftovers.Count == 0)
            return;

        var hasPriorWork = required.Any(a =>
            a.Status == AssignmentStatus.Completed
            && a.ActivityDate is not null
            && a.ActivityDate < today);
        if (!hasPriorWork)
            return;

        await CloseDayAsPartialSlidingLeftoversAsync(inProgress, required);
    }

    public async Task<PlannedDay?> EnsureRolloverAndActivateAsync(int studentId)
    {
        await MaybeRolloverStaleInProgressAsync(studentId);
        return await GetOrActivateCurrentDayAsync(studentId);
    }

    public async Task SlideUnfinishedRequiredOffDayAsync(PlannedDay day)
    {
        var leftovers = await db.Assignments
            .Where(a => a.PlannedDayId == day.Id
                        && a.Kind == AssignmentKind.Required
                        && (a.Status == AssignmentStatus.Assigned
                            || a.Status == AssignmentStatus.DeferRequested))
            .OrderBy(a => a.Id)
            .ToListAsync();

        foreach (var leftover in leftovers)
            await SlideAssignmentForwardAsync(leftover, day);
    }

    private async Task CloseDayAsPartialSlidingLeftoversAsync(PlannedDay day, List<Assignment> requiredBeforeSlide)
    {
        var completedDates = requiredBeforeSlide
            .Where(a => a.Status == AssignmentStatus.Completed && a.ActivityDate is not null)
            .Select(a => a.ActivityDate!.Value)
            .ToList();
        var calendarDate = completedDates.Count > 0
            ? completedDates.Max()
            : DateOnly.FromDateTime(DateTime.Today);

        await SlideUnfinishedRequiredOffDayAsync(day);

        day.Status = PlannedDayStatus.PartiallyCompleted;
        day.CalendarDate = calendarDate;
        day.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task SlideAssignmentForwardAsync(Assignment assignment, PlannedDay fromDay)
    {
        var laterDays = await db.PlannedDays
            .Where(d => d.StudentUserId == assignment.StudentUserId
                        && d.SequenceIndex > fromDay.SequenceIndex
                        && d.Status != PlannedDayStatus.Completed
                        && d.Status != PlannedDayStatus.PartiallyCompleted)
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
                .MaxAsync() ?? fromDay.SequenceIndex;

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
            // Keep pending parent requests on the Requests page when leftovers
            // slide during rollover or a parent-forced full-day close.
            if (chain[i].Status != AssignmentStatus.DeferRequested)
                chain[i].Status = AssignmentStatus.Assigned;
            chain[i].ActivityDate = null;
        }

        // Persist slides first so later leftover slides and day-completion queries see new PlannedDayId values.
        await db.SaveChangesAsync();
    }

    private async Task<PlannedDay?> GetOrActivateCurrentDayAsync(int studentId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var inProgress = await db.PlannedDays
            .Where(d => d.StudentUserId == studentId && d.Status == PlannedDayStatus.InProgress)
            .OrderBy(d => d.SequenceIndex)
            .FirstOrDefaultAsync();
        if (inProgress is not null)
            return inProgress;

        // Keep showing a day completed today so the student still sees what they finished.
        var completedToday = await db.PlannedDays
            .Where(d => d.StudentUserId == studentId
                        && d.Status == PlannedDayStatus.Completed
                        && d.CalendarDate == today)
            .OrderByDescending(d => d.SequenceIndex)
            .FirstOrDefaultAsync();
        if (completedToday is not null)
            return completedToday;

        var planned = await db.PlannedDays
            .Where(d => d.StudentUserId == studentId && d.Status == PlannedDayStatus.Planned)
            .OrderBy(d => d.SequenceIndex)
            .FirstOrDefaultAsync();
        if (planned is not null)
        {
            planned.Status = PlannedDayStatus.InProgress;
            await db.SaveChangesAsync();
            return planned;
        }

        // No further planned work — fall back to the most recent closed day.
        return await db.PlannedDays
            .Where(d => d.StudentUserId == studentId
                        && (d.Status == PlannedDayStatus.Completed
                            || d.Status == PlannedDayStatus.PartiallyCompleted))
            .OrderByDescending(d => d.SequenceIndex)
            .FirstOrDefaultAsync();
    }
}
