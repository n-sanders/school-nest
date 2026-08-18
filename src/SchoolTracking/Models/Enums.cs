namespace SchoolTracking.Models;

public enum UserRole
{
    Parent,
    Student
}

public enum EffortLevel
{
    Low,
    High
}

public enum PlannedDayStatus
{
    Planned,
    InProgress,
    Completed,
    PartiallyCompleted
}

public static class PlannedDayStatuses
{
    public static bool IsClosed(PlannedDayStatus status) =>
        status is PlannedDayStatus.Completed or PlannedDayStatus.PartiallyCompleted;
}

public enum AssignmentKind
{
    Required,
    Optional
}

public enum AssignmentStatus
{
    Assigned,
    Completed,
    DeferRequested,
    Deferred
}

public static class EffortMinutes
{
    public const int Low = 30;
    public const int High = 60;

    public static int ToMinutes(EffortLevel effort) =>
        effort == EffortLevel.High ? High : Low;
}
