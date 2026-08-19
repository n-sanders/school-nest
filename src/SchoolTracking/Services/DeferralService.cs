using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Models;

namespace SchoolTracking.Services;

public class DeferralService(AppDbContext db)
{
    /// <summary>
    /// Approve deferral: move assignment to next planned day for same course,
    /// shifting that course's later required items one slot forward.
    /// Does not start a Planned day — only leftover confirmation or first complete does.
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

        await SlideAssignmentForwardAsync(assignment, currentDay, CarryoverKind.Deferred);
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
        day.CalendarDate ??= day.StartedOn ?? DateOnly.FromDateTime(DateTime.Today);
        day.StartedOn ??= day.CalendarDate;
        day.CompletedAt = DateTime.UtcNow;

        foreach (var a in required.Where(a => a.Status == AssignmentStatus.Completed && a.ActivityDate is null))
            a.ActivityDate = day.CalendarDate;
    }

    public void StartDayOnFirstComplete(PlannedDay day)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (day.Status == PlannedDayStatus.Planned)
            day.Status = PlannedDayStatus.InProgress;
        day.StartedOn ??= today;
    }

    public async Task<PendingLeftovers?> GetPendingLeftoversAsync(int studentId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var inProgress = await db.PlannedDays
            .Where(d => d.StudentUserId == studentId && d.Status == PlannedDayStatus.InProgress)
            .OrderBy(d => d.SequenceIndex)
            .FirstOrDefaultAsync();
        if (inProgress is null)
            return null;

        // Prefer StartedOn; fall back for sparse/legacy rows that never got anchored.
        if (inProgress.StartedOn is not null)
        {
            if (inProgress.StartedOn >= today)
                return null;
        }
        else
        {
            var startedBeforeToday = await db.Assignments.AnyAsync(a =>
                a.PlannedDayId == inProgress.Id
                && a.Kind == AssignmentKind.Required
                && a.Status == AssignmentStatus.Completed
                && a.ActivityDate != null
                && a.ActivityDate < today);
            if (!startedBeforeToday)
                return null;
        }

        var leftovers = await db.Assignments
            .Include(a => a.Course).ThenInclude(c => c!.Subject)
            .Where(a => a.PlannedDayId == inProgress.Id
                        && a.Kind == AssignmentKind.Required
                        && (a.Status == AssignmentStatus.Assigned
                            || a.Status == AssignmentStatus.DeferRequested))
            .OrderBy(a => a.Course!.Subject.SortOrder)
            .ThenBy(a => a.Course!.SortOrder)
            .ToListAsync();
        if (leftovers.Count == 0)
            return null;

        return new PendingLeftovers(inProgress, leftovers);
    }

    public async Task<(bool ok, string? error)> ResolveLeftoversAsync(
        int studentId,
        IReadOnlyCollection<int> completedIds,
        IReadOnlyDictionary<int, EffortLevel> efforts)
    {
        var pending = await GetPendingLeftoversAsync(studentId);
        if (pending is null)
            return (false, "There is no leftover work to confirm");

        var leftoverIds = pending.Assignments.Select(a => a.Id).ToHashSet();
        if (completedIds.Any(id => !leftoverIds.Contains(id)))
            return (false, "One or more assignments are not leftover work on the started day");

        var day = pending.Day;
        var startedOn = day.StartedOn ?? DateOnly.FromDateTime(DateTime.Today);

        foreach (var assignment in pending.Assignments.Where(a => completedIds.Contains(a.Id)))
        {
            if (efforts.TryGetValue(assignment.Id, out var effort))
                assignment.Effort = effort;
            assignment.Status = AssignmentStatus.Completed;
            assignment.CompletedAt = DateTime.UtcNow;
            assignment.ActivityDate = startedOn;
        }

        await db.SaveChangesAsync();

        var remaining = await db.Assignments
            .Where(a => a.PlannedDayId == day.Id
                        && a.Kind == AssignmentKind.Required
                        && (a.Status == AssignmentStatus.Assigned
                            || a.Status == AssignmentStatus.DeferRequested))
            .OrderBy(a => a.Id)
            .ToListAsync();

        if (remaining.Count == 0)
        {
            await MaybeCompleteDayAsync(day);
            await db.SaveChangesAsync();
            return (true, null);
        }

        await CloseDayAsPartialSlidingLeftoversAsync(day);
        return (true, null);
    }

    /// <summary>
    /// Current slot to show on Today after leftovers are resolved.
    /// Does not start a Planned day and does not slide leftovers.
    /// </summary>
    public async Task<PlannedDay?> GetCurrentDayAsync(int studentId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var inProgress = await db.PlannedDays
            .Where(d => d.StudentUserId == studentId && d.Status == PlannedDayStatus.InProgress)
            .OrderBy(d => d.SequenceIndex)
            .FirstOrDefaultAsync();
        if (inProgress is not null)
        {
            await MaybeCompleteDayAsync(inProgress);
            await db.SaveChangesAsync();
            if (inProgress.Status == PlannedDayStatus.InProgress)
                return inProgress;
        }

        // Inline closed statuses — EF cannot translate PlannedDayStatuses.IsClosed().
        var closedToday = await db.PlannedDays
            .Where(d => d.StudentUserId == studentId
                        && (d.Status == PlannedDayStatus.Completed
                            || d.Status == PlannedDayStatus.PartiallyCompleted)
                        && d.CalendarDate == today)
            .OrderByDescending(d => d.SequenceIndex)
            .FirstOrDefaultAsync();
        if (closedToday is not null)
            return closedToday;

        var planned = await db.PlannedDays
            .Where(d => d.StudentUserId == studentId && d.Status == PlannedDayStatus.Planned)
            .OrderBy(d => d.SequenceIndex)
            .FirstOrDefaultAsync();
        if (planned is not null)
            return planned;

        return await db.PlannedDays
            .Where(d => d.StudentUserId == studentId
                        && (d.Status == PlannedDayStatus.Completed
                            || d.Status == PlannedDayStatus.PartiallyCompleted))
            .OrderByDescending(d => d.SequenceIndex)
            .FirstOrDefaultAsync();
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
            await SlideAssignmentForwardAsync(leftover, day, CarryoverKind.Leftover);
    }

    private async Task CloseDayAsPartialSlidingLeftoversAsync(PlannedDay day)
    {
        await SlideUnfinishedRequiredOffDayAsync(day);

        day.Status = PlannedDayStatus.PartiallyCompleted;
        day.CalendarDate = day.StartedOn ?? DateOnly.FromDateTime(DateTime.Today);
        day.StartedOn ??= day.CalendarDate;
        day.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task SlideAssignmentForwardAsync(
        Assignment assignment,
        PlannedDay fromDay,
        CarryoverKind carryoverKind)
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
            chain[i].ActivityDate = null;
            if (i == 0)
            {
                // Keep pending parent requests on the Requests page when leftovers slide.
                if (chain[0].Status != AssignmentStatus.DeferRequested)
                    chain[0].Status = AssignmentStatus.Assigned;
                chain[0].CarryoverKind = carryoverKind;
                chain[0].SourcePlannedDayId = fromDay.Id;
            }
            else if (chain[i].Status != AssignmentStatus.DeferRequested)
            {
                chain[i].Status = AssignmentStatus.Assigned;
            }
        }

        await db.SaveChangesAsync();
    }

    public record PendingLeftovers(PlannedDay Day, List<Assignment> Assignments);
}
