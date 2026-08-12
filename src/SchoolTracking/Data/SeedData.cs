using Microsoft.EntityFrameworkCore;
using SchoolTracking.Models;
using SchoolTracking.Services;

namespace SchoolTracking.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        // MVP has no migrations: recreate DB when schema is missing the optional-activities table.
        if (await db.Database.CanConnectAsync())
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='OptionalActivities'";
            var hasOptionalTable = await cmd.ExecuteScalarAsync() is not null;
            await conn.CloseAsync();

            if (!hasOptionalTable)
                await db.Database.EnsureDeletedAsync();
        }

        await db.Database.EnsureCreatedAsync();
        await SchemaPatches.ApplyAsync(db);

        // Existing DBs: give empty courses a Next lesson so they become plannable.
        if (await db.Families.AnyAsync())
        {
            var families = await db.Families.ToListAsync();
            foreach (var existing in families)
            {
                if (string.IsNullOrWhiteSpace(existing.ImageGenBoilerplate))
                    existing.ImageGenBoilerplate = ImageGen.DefaultBoilerplate;
                if (string.IsNullOrWhiteSpace(existing.ImageGenModel))
                    existing.ImageGenModel = ImageGen.DefaultModel;
                if (existing.ImageGenDailyLimit <= 0)
                    existing.ImageGenDailyLimit = ImageGen.DefaultDailyLimit;
            }
            await db.SaveChangesAsync();
            await CatalogDefaults.EnsureNextLessonForEmptyCoursesAsync(db);
            return;
        }

        var family = new Family
        {
            Name = "Sanders Family",
            TargetHoursPerYear = 900,
            CreatedAt = DateTime.UtcNow,
            ImageGenDailyLimit = ImageGen.DefaultDailyLimit,
            ImageGenBoilerplate = ImageGen.DefaultBoilerplate,
            ImageGenModel = ImageGen.DefaultModel
        };
        db.Families.Add(family);
        await db.SaveChangesAsync();

        db.Users.AddRange(
            new User { FamilyId = family.Id, DisplayName = "Mama", Role = UserRole.Parent, MagicWord = "kate", IsActive = true },
            new User { FamilyId = family.Id, DisplayName = "Papa", Role = UserRole.Parent, MagicWord = "nate", IsActive = true },
            new User { FamilyId = family.Id, DisplayName = "Evie", Role = UserRole.Student, MagicWord = "bearcat", IsActive = true },
            new User { FamilyId = family.Id, DisplayName = "Noah", Role = UserRole.Student, MagicWord = "spacex", IsActive = true },
            new User { FamilyId = family.Id, DisplayName = "Hannah", Role = UserRole.Student, MagicWord = "tater", IsActive = true },
            new User { FamilyId = family.Id, DisplayName = "Judah", Role = UserRole.Student, MagicWord = "minecraft", IsActive = true },
            new User { FamilyId = family.Id, DisplayName = "Ezra", Role = UserRole.Student, MagicWord = "cat", IsActive = true }
        );
        await db.SaveChangesAsync();

        var math = new Subject { FamilyId = family.Id, Name = "Math", SortOrder = 1 };
        var language = new Subject { FamilyId = family.Id, Name = "Language", SortOrder = 2 };
        var history = new Subject { FamilyId = family.Id, Name = "History", SortOrder = 3 };
        var science = new Subject { FamilyId = family.Id, Name = "Science", SortOrder = 4 };
        var music = new Subject { FamilyId = family.Id, Name = "Music", SortOrder = 5 };
        db.Subjects.AddRange(math, language, history, science, music);
        await db.SaveChangesAsync();

        var gbMath3 = new Course { SubjectId = math.Id, Name = "G&B Math 3", SortOrder = 1 };
        var gbMath4 = new Course { SubjectId = math.Id, Name = "G&B Math 4", SortOrder = 2 };
        var mathAcademy = new Course { SubjectId = math.Id, Name = "Math Academy", SortOrder = 3 };
        var mathArcade = new Course { SubjectId = math.Id, Name = "Math Arcade", SortOrder = 4 };

        var gbLang3 = new Course { SubjectId = language.Id, Name = "G&B Language 3", SortOrder = 1 };
        var gbLang5 = new Course { SubjectId = language.Id, Name = "G&B Language 5", SortOrder = 2 };
        var gbLang6 = new Course { SubjectId = language.Id, Name = "G&B Language 6", SortOrder = 3 };

        var papaHistory = new Course { SubjectId = history.Id, Name = "Papa History", SortOrder = 1 };
        var usHistory = new Course { SubjectId = history.Id, Name = "US History", SortOrder = 2 };

        var scienceWeird = new Course { SubjectId = science.Id, Name = "Science is Weird", SortOrder = 1 };
        var gbBiology = new Course { SubjectId = science.Id, Name = "G&B Biology", SortOrder = 2 };
        var physics = new Course { SubjectId = science.Id, Name = "Physics", SortOrder = 3 };

        var trumpet = new Course { SubjectId = music.Id, Name = "Trumpet", SortOrder = 1 };
        var flute = new Course { SubjectId = music.Id, Name = "Flute", SortOrder = 2 };

        var courses = new[]
        {
            gbMath3, gbMath4, mathAcademy, mathArcade,
            gbLang3, gbLang5, gbLang6,
            papaHistory, usHistory,
            scienceWeird, gbBiology, physics,
            trumpet, flute
        };
        db.Courses.AddRange(courses);
        await db.SaveChangesAsync();

        // Every course gets a reusable Next lesson; a few keep specific extras.
        foreach (var course in courses)
            CatalogDefaults.AddNextLesson(db, course.Id);

        db.CatalogAssignments.AddRange(
            new CatalogAssignment { CourseId = flute.Id, Name = "Practice", DefaultEffort = EffortLevel.Low, SortOrder = 1 },
            new CatalogAssignment { CourseId = flute.Id, Name = "Lesson", DefaultEffort = EffortLevel.High, SortOrder = 2 },
            new CatalogAssignment { CourseId = gbBiology.Id, Name = "Lesson 1", DefaultEffort = EffortLevel.High, SortOrder = 1 },
            new CatalogAssignment { CourseId = mathAcademy.Id, Name = "30 XP", DefaultEffort = EffortLevel.High, SortOrder = 1 },
            new CatalogAssignment { CourseId = mathArcade.Id, Name = "7 Activities", DefaultEffort = EffortLevel.Low, SortOrder = 1 }
        );

        db.OptionalActivities.AddRange(
            new OptionalActivity { FamilyId = family.Id, Name = "Free reading", DefaultEffort = EffortLevel.Low, SortOrder = 1 },
            new OptionalActivity { FamilyId = family.Id, Name = "Nature walk", DefaultEffort = EffortLevel.Low, SortOrder = 2 },
            new OptionalActivity { FamilyId = family.Id, Name = "Extra practice", DefaultEffort = EffortLevel.Low, SortOrder = 3 }
        );

        await db.SaveChangesAsync();
    }
}
