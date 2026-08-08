using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Models;
using SchoolTracking.Services;

namespace SchoolTracking.Endpoints;

public static class ReportEndpoints
{
    public static RouteGroupBuilder MapReportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/reports");

        group.MapGet("/family", async (AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var family = await db.Families.FirstAsync(f => f.Id == user.FamilyId);
            return Results.Ok(new
            {
                family.Id,
                family.Name,
                family.TargetHoursPerYear
            });
        });

        group.MapPut("/family/target-hours", async (TargetHoursRequest req, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;
            if (req.TargetHoursPerYear < 0)
                return Results.BadRequest(new { error = "Invalid target" });

            var family = await db.Families.FirstAsync(f => f.Id == user.FamilyId);
            family.TargetHoursPerYear = req.TargetHoursPerYear;
            await db.SaveChangesAsync();
            return Results.Ok(new { family.TargetHoursPerYear });
        });

        group.MapGet("/{studentId:int}", async (
            int studentId,
            AuthService auth,
            HttpContext http,
            AppDbContext db,
            string? from,
            string? to) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var student = await db.Users.FirstOrDefaultAsync(u =>
                u.Id == studentId && u.FamilyId == user.FamilyId && u.Role == UserRole.Student);
            if (student is null)
                return Results.NotFound(new { error = "Student not found" });

            var family = await db.Families.FirstAsync(f => f.Id == user.FamilyId);

            var end = DateOnly.FromDateTime(DateTime.Today);
            var start = end.AddMonths(-12).AddDays(1);
            if (!string.IsNullOrWhiteSpace(from) && DateOnly.TryParse(from, out var f))
                start = f;
            if (!string.IsNullOrWhiteSpace(to) && DateOnly.TryParse(to, out var t))
                end = t;

            var assignments = await db.Assignments
                .Include(a => a.Course).ThenInclude(c => c.Subject)
                .Where(a => a.StudentUserId == studentId
                            && a.Status == AssignmentStatus.Completed
                            && a.ActivityDate != null
                            && a.ActivityDate >= start
                            && a.ActivityDate <= end)
                .ToListAsync();

            var hourEligible = assignments.Where(AssignmentHelpers.CountsTowardHours).ToList();

            var completedDays = await db.PlannedDays
                .Where(d => d.StudentUserId == studentId
                            && d.Status == PlannedDayStatus.Completed
                            && d.CalendarDate != null
                            && d.CalendarDate >= start
                            && d.CalendarDate <= end)
                .ToListAsync();

            var fullDayDates = completedDays.Select(d => d.CalendarDate!.Value).ToHashSet();

            var byDate = hourEligible
                .GroupBy(a => a.ActivityDate!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        minutes = g.Sum(a => EffortMinutes.ToMinutes(a.Effort)),
                        requiredMinutes = g.Where(a => a.Kind == AssignmentKind.Required).Sum(a => EffortMinutes.ToMinutes(a.Effort)),
                        optionalMinutes = g.Where(a => a.Kind == AssignmentKind.Optional).Sum(a => EffortMinutes.ToMinutes(a.Effort)),
                        assignmentCount = g.Count()
                    });

            var allDates = byDate.Keys.Union(fullDayDates).OrderByDescending(d => d).ToList();

            var calendar = allDates.Select(d =>
            {
                byDate.TryGetValue(d, out var stats);
                var minutes = stats?.minutes ?? 0;
                return new
                {
                    date = d.ToString("yyyy-MM-dd"),
                    isFullDay = fullDayDates.Contains(d),
                    minutes,
                    hours = Math.Round(minutes / 60.0, 2),
                    requiredMinutes = stats?.requiredMinutes ?? 0,
                    optionalMinutes = stats?.optionalMinutes ?? 0,
                    assignmentCount = stats?.assignmentCount ?? 0
                };
            });

            var totalMinutes = hourEligible.Sum(a => EffortMinutes.ToMinutes(a.Effort));
            var totalHours = Math.Round(totalMinutes / 60.0, 2);

            return Results.Ok(new
            {
                student = new { student.Id, student.DisplayName },
                range = new { from = start.ToString("yyyy-MM-dd"), to = end.ToString("yyyy-MM-dd") },
                targetHoursPerYear = family.TargetHoursPerYear,
                totalMinutes,
                totalHours,
                hoursRemaining = Math.Round(Math.Max(0, family.TargetHoursPerYear - totalHours), 2),
                fullDayCount = fullDayDates.Count,
                activeDayCount = allDates.Count,
                calendar
            });
        });

        return group;
    }

    public record TargetHoursRequest(int TargetHoursPerYear);
}
