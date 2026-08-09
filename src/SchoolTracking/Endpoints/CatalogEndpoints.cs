using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Models;
using SchoolTracking.Services;

namespace SchoolTracking.Endpoints;

public static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/catalog");

        group.MapGet("/tree", async (AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireUserAsync(auth);
            if (user is null) return Results.Empty;

            var subjects = await db.Subjects
                .Where(s => s.FamilyId == user.FamilyId)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Name)
                .Include(s => s.Courses.OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
                .ThenInclude(c => c.CatalogAssignments.OrderBy(a => a.SortOrder).ThenBy(a => a.Name))
                .ToListAsync();

            return Results.Ok(subjects.Select(s => new
            {
                s.Id,
                s.Name,
                s.SortOrder,
                courses = s.Courses.Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.SortOrder,
                    assignments = c.CatalogAssignments.Select(a => new
                    {
                        a.Id,
                        a.Name,
                        a.Url,
                        a.Description,
                        defaultEffort = a.DefaultEffort.ToString().ToLowerInvariant(),
                        a.SortOrder
                    })
                })
            }));
        });

        group.MapPost("/subjects", async (SubjectRequest req, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "Name required" });

            var max = await db.Subjects.Where(s => s.FamilyId == user.FamilyId).MaxAsync(s => (int?)s.SortOrder) ?? 0;
            var subject = new Subject
            {
                FamilyId = user.FamilyId,
                Name = req.Name.Trim(),
                SortOrder = req.SortOrder ?? max + 1
            };
            db.Subjects.Add(subject);
            await db.SaveChangesAsync();
            return Results.Ok(new { subject.Id, subject.Name, subject.SortOrder });
        });

        group.MapPost("/courses", async (CourseRequest req, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "Name required" });

            var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == req.SubjectId && s.FamilyId == user.FamilyId);
            if (subject is null)
                return Results.NotFound(new { error = "Subject not found" });

            var max = await db.Courses.Where(c => c.SubjectId == subject.Id).MaxAsync(c => (int?)c.SortOrder) ?? 0;
            var course = new Course
            {
                SubjectId = subject.Id,
                Name = req.Name.Trim(),
                SortOrder = req.SortOrder ?? max + 1
            };
            db.Courses.Add(course);
            await db.SaveChangesAsync();
            CatalogDefaults.AddNextLesson(db, course.Id);
            await db.SaveChangesAsync();
            return Results.Ok(new { course.Id, course.SubjectId, course.Name, course.SortOrder });
        });

        group.MapPost("/assignments", async (CatalogAssignmentRequest req, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "Name required" });

            var course = await db.Courses.Include(c => c.Subject)
                .FirstOrDefaultAsync(c => c.Id == req.CourseId && c.Subject.FamilyId == user.FamilyId);
            if (course is null)
                return Results.NotFound(new { error = "Course not found" });

            if (!AssignmentHelpers.TryParseEffort(req.DefaultEffort ?? "low", out var effort))
                return Results.BadRequest(new { error = "defaultEffort must be low or high" });

            var max = await db.CatalogAssignments.Where(a => a.CourseId == course.Id).MaxAsync(a => (int?)a.SortOrder) ?? 0;
            var item = new CatalogAssignment
            {
                CourseId = course.Id,
                Name = req.Name.Trim(),
                Url = string.IsNullOrWhiteSpace(req.Url) ? null : req.Url.Trim(),
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                DefaultEffort = effort,
                SortOrder = req.SortOrder ?? max + 1
            };
            db.CatalogAssignments.Add(item);
            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                item.Id,
                item.CourseId,
                item.Name,
                item.Url,
                item.Description,
                defaultEffort = item.DefaultEffort.ToString().ToLowerInvariant(),
                item.SortOrder
            });
        });

        group.MapPut("/assignments/{id:int}", async (int id, CatalogAssignmentRequest req, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var item = await db.CatalogAssignments.Include(a => a.Course).ThenInclude(c => c.Subject)
                .FirstOrDefaultAsync(a => a.Id == id && a.Course.Subject.FamilyId == user.FamilyId);
            if (item is null)
                return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(req.Name))
                item.Name = req.Name.Trim();
            if (req.Url is not null)
                item.Url = string.IsNullOrWhiteSpace(req.Url) ? null : req.Url.Trim();
            if (req.Description is not null)
                item.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
            if (req.DefaultEffort is not null)
            {
                if (!AssignmentHelpers.TryParseEffort(req.DefaultEffort, out var effort))
                    return Results.BadRequest(new { error = "defaultEffort must be low or high" });
                item.DefaultEffort = effort;
            }
            if (req.SortOrder is not null)
                item.SortOrder = req.SortOrder.Value;

            await db.SaveChangesAsync();
            return Results.Ok(new { item.Id, ok = true });
        });

        group.MapGet("/students", async (AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var students = await db.Users
                .Where(u => u.FamilyId == user.FamilyId && u.Role == UserRole.Student && u.IsActive)
                .OrderBy(u => u.DisplayName)
                .Select(u => new { u.Id, u.DisplayName })
                .ToListAsync();
            return Results.Ok(students);
        });

        return group;
    }

    public record SubjectRequest(string Name, int? SortOrder);
    public record CourseRequest(int SubjectId, string Name, int? SortOrder);
    public record CatalogAssignmentRequest(int CourseId, string? Name, string? Url, string? Description, string? DefaultEffort, int? SortOrder);
}
