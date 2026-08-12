namespace SchoolTracking.Models;

public class Family
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int TargetHoursPerYear { get; set; } = 900;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? OpenRouterApiKey { get; set; }
    public int ImageGenDailyLimit { get; set; } = 3;
    public string ImageGenBoilerplate { get; set; } = "";
    public string ImageGenModel { get; set; } = "";

    public List<User> Users { get; set; } = [];
    public List<Subject> Subjects { get; set; } = [];
    public List<OptionalActivity> OptionalActivities { get; set; } = [];
    public List<GeneratedBackground> GeneratedBackgrounds { get; set; } = [];
    public List<RejectedImagePrompt> RejectedImagePrompts { get; set; } = [];
}

public class User
{
    public int Id { get; set; }
    public int FamilyId { get; set; }
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; }
    public string MagicWord { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int? ActiveBackgroundId { get; set; }

    public Family Family { get; set; } = null!;
    public GeneratedBackground? ActiveBackground { get; set; }
    public List<Session> Sessions { get; set; } = [];
    public List<PlannedDay> PlannedDays { get; set; } = [];
    public List<Assignment> Assignments { get; set; } = [];
    public List<GeneratedBackground> GeneratedBackgrounds { get; set; } = [];
    public List<RejectedImagePrompt> RejectedImagePrompts { get; set; } = [];
}

public class Session
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; } = "";
    public DateTime ExpiresAt { get; set; }

    public User User { get; set; } = null!;
}

public class Subject
{
    public int Id { get; set; }
    public int FamilyId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }

    public Family Family { get; set; } = null!;
    public List<Course> Courses { get; set; } = [];
}

public class Course
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }

    public Subject Subject { get; set; } = null!;
    public List<CatalogAssignment> CatalogAssignments { get; set; } = [];
}

public class CatalogAssignment
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string Name { get; set; } = "";
    public string? Url { get; set; }
    public string? Description { get; set; }
    public EffortLevel DefaultEffort { get; set; } = EffortLevel.Low;
    public int SortOrder { get; set; }

    public Course Course { get; set; } = null!;
}

/// <summary>
/// Family-wide optional activity presets (not tied to subject/course).
/// </summary>
public class OptionalActivity
{
    public int Id { get; set; }
    public int FamilyId { get; set; }
    public string Name { get; set; } = "";
    public string? Url { get; set; }
    public string? Description { get; set; }
    public EffortLevel DefaultEffort { get; set; } = EffortLevel.Low;
    public int SortOrder { get; set; }

    public Family Family { get; set; } = null!;
}

public class PlannedDay
{
    public int Id { get; set; }
    public int StudentUserId { get; set; }
    public int SequenceIndex { get; set; }
    public PlannedDayStatus Status { get; set; } = PlannedDayStatus.Planned;
    public DateOnly? CalendarDate { get; set; }
    public DateTime? CompletedAt { get; set; }

    public User Student { get; set; } = null!;
    public List<Assignment> Assignments { get; set; } = [];
}

public class Assignment
{
    public int Id { get; set; }
    public int StudentUserId { get; set; }
    public int? CourseId { get; set; }
    public int? CatalogAssignmentId { get; set; }
    public int? OptionalActivityId { get; set; }
    public int? PlannedDayId { get; set; }
    public string Name { get; set; } = "";
    public string? Url { get; set; }
    public string? Description { get; set; }
    public EffortLevel Effort { get; set; } = EffortLevel.Low;
    public AssignmentKind Kind { get; set; } = AssignmentKind.Required;
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Assigned;
    public DateTime? CompletedAt { get; set; }
    public DateTime? HoursAcknowledgedAt { get; set; }
    public DateOnly? ActivityDate { get; set; }

    public User Student { get; set; } = null!;
    public Course? Course { get; set; }
    public CatalogAssignment? CatalogAssignment { get; set; }
    public OptionalActivity? OptionalActivity { get; set; }
    public PlannedDay? PlannedDay { get; set; }
}

public class GeneratedBackground
{
    public int Id { get; set; }
    public int FamilyId { get; set; }
    public int StudentUserId { get; set; }
    public string StudentPrompt { get; set; } = "";
    public byte[] ImageBytes { get; set; } = [];
    public string ContentType { get; set; } = "image/png";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Family Family { get; set; } = null!;
    public User Student { get; set; } = null!;
}

public class RejectedImagePrompt
{
    public int Id { get; set; }
    public int FamilyId { get; set; }
    public int StudentUserId { get; set; }
    public string StudentPrompt { get; set; } = "";
    public string? ProviderMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Family Family { get; set; } = null!;
    public User Student { get; set; } = null!;
}
