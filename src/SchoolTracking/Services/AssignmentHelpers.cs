using SchoolTracking.Models;

namespace SchoolTracking.Services;

public static class AssignmentHelpers
{
    public static bool CountsTowardHours(Assignment a)
    {
        if (a.Status != AssignmentStatus.Completed)
            return false;
        if (a.Kind == AssignmentKind.Required)
            return true;
        return a.HoursAcknowledgedAt is not null;
    }

    public static object ToDto(Assignment a, Course? course = null, Subject? subject = null, PlannedDay? day = null) => new
    {
        a.Id,
        a.StudentUserId,
        a.CourseId,
        a.OptionalActivityId,
        courseName = a.Kind == AssignmentKind.Optional ? "Optional" : course?.Name,
        subjectName = a.Kind == AssignmentKind.Optional ? "Optional" : subject?.Name,
        a.CatalogAssignmentId,
        a.PlannedDayId,
        plannedDaySequence = day?.SequenceIndex,
        a.Name,
        a.Url,
        a.Description,
        effort = a.Effort.ToString().ToLowerInvariant(),
        minutes = EffortMinutes.ToMinutes(a.Effort),
        kind = a.Kind.ToString().ToLowerInvariant(),
        status = ToStatusString(a.Status),
        a.CompletedAt,
        a.HoursAcknowledgedAt,
        activityDate = a.ActivityDate?.ToString("yyyy-MM-dd"),
        countsTowardHours = CountsTowardHours(a)
    };

    public static string ToStatusString(AssignmentStatus status) => status switch
    {
        AssignmentStatus.Assigned => "assigned",
        AssignmentStatus.Completed => "completed",
        AssignmentStatus.DeferRequested => "defer_requested",
        AssignmentStatus.Deferred => "deferred",
        _ => status.ToString().ToLowerInvariant()
    };

    public static bool TryParseEffort(string? value, out EffortLevel effort)
    {
        effort = EffortLevel.Low;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        switch (value.Trim().ToLowerInvariant())
        {
            case "low":
                effort = EffortLevel.Low;
                return true;
            case "high":
                effort = EffortLevel.High;
                return true;
            default:
                return false;
        }
    }
}
