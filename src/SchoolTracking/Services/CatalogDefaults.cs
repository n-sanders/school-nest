using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Models;

namespace SchoolTracking.Services;

public static class CatalogDefaults
{
    public const string NextLessonName = "Next lesson";
    public const int NextLessonSortOrder = 0;

    public static CatalogAssignment CreateNextLesson(int courseId) => new()
    {
        CourseId = courseId,
        Name = NextLessonName,
        DefaultEffort = EffortLevel.Low,
        SortOrder = NextLessonSortOrder
    };

    public static void AddNextLesson(AppDbContext db, int courseId)
    {
        db.CatalogAssignments.Add(CreateNextLesson(courseId));
    }

    /// <summary>
    /// Startup repair: ensure every course has at least one catalog item (Next lesson).
    /// </summary>
    public static async Task EnsureNextLessonForEmptyCoursesAsync(AppDbContext db)
    {
        var emptyCourseIds = await db.Courses
            .Where(c => !c.CatalogAssignments.Any())
            .Select(c => c.Id)
            .ToListAsync();

        if (emptyCourseIds.Count == 0)
            return;

        foreach (var courseId in emptyCourseIds)
            AddNextLesson(db, courseId);

        await db.SaveChangesAsync();
    }
}
