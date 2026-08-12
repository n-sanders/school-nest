using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Models;
using SchoolTracking.Services;

namespace SchoolTracking.Endpoints;

public static class FamilyEndpoints
{
    public static RouteGroupBuilder MapFamilyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/family");

        group.MapGet("/magic-words", async (AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var users = await db.Users
                .Where(u => u.FamilyId == user.FamilyId && u.IsActive)
                .OrderBy(u => u.Role)
                .ThenBy(u => u.DisplayName)
                .Select(u => new
                {
                    u.Id,
                    u.DisplayName,
                    role = u.Role.ToString().ToLowerInvariant(),
                    magicWord = u.MagicWord,
                    isSelf = u.Id == user.Id
                })
                .ToListAsync();

            return Results.Ok(users);
        });

        group.MapPut("/magic-words/{userId:int}", async (
            int userId, MagicWordRequest req, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            if (string.IsNullOrWhiteSpace(req.MagicWord))
                return Results.BadRequest(new { error = "magicWord required" });

            var target = await db.Users.FirstOrDefaultAsync(u =>
                u.Id == userId && u.FamilyId == user.FamilyId && u.IsActive);
            if (target is null)
                return Results.NotFound(new { error = "User not found" });

            // Parents may change their own word or any student's word (not another parent's).
            if (target.Role == UserRole.Parent && target.Id != user.Id)
                return Results.BadRequest(new { error = "You can only change your own parent magic word, or a student's" });

            target.MagicWord = req.MagicWord.Trim();
            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                target.Id,
                target.DisplayName,
                role = target.Role.ToString().ToLowerInvariant(),
                magicWord = target.MagicWord
            });
        });

        group.MapGet("/optional-activities", async (AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireUserAsync(auth);
            if (user is null) return Results.Empty;

            var items = await db.OptionalActivities
                .Where(a => a.FamilyId == user.FamilyId)
                .OrderBy(a => a.SortOrder)
                .ThenBy(a => a.Name)
                .Select(a => new
                {
                    a.Id,
                    a.Name,
                    a.Url,
                    a.Description,
                    defaultEffort = a.DefaultEffort.ToString().ToLowerInvariant(),
                    a.SortOrder
                })
                .ToListAsync();
            return Results.Ok(items);
        });

        group.MapPost("/optional-activities", async (
            OptionalActivityRequest req, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireUserAsync(auth);
            if (user is null) return Results.Empty;
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "Name required" });

            EffortLevel effort = EffortLevel.Low;
            if (req.DefaultEffort is not null && !AssignmentHelpers.TryParseEffort(req.DefaultEffort, out effort))
                return Results.BadRequest(new { error = "defaultEffort must be low or high" });

            var max = await db.OptionalActivities.Where(a => a.FamilyId == user.FamilyId)
                .MaxAsync(a => (int?)a.SortOrder) ?? 0;

            var item = new OptionalActivity
            {
                FamilyId = user.FamilyId,
                Name = req.Name.Trim(),
                Url = string.IsNullOrWhiteSpace(req.Url) ? null : req.Url.Trim(),
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                DefaultEffort = effort,
                SortOrder = max + 1
            };
            db.OptionalActivities.Add(item);
            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                item.Id,
                item.Name,
                item.Url,
                item.Description,
                defaultEffort = item.DefaultEffort.ToString().ToLowerInvariant(),
                item.SortOrder
            });
        });

        group.MapGet("/image-settings", async (AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var family = await db.Families.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == user.FamilyId);
            if (family is null)
                return Results.NotFound(new { error = "Family not found" });

            return Results.Ok(new
            {
                apiKeyMasked = ImageGen.MaskApiKey(family.OpenRouterApiKey),
                hasApiKey = !string.IsNullOrWhiteSpace(family.OpenRouterApiKey),
                dailyLimit = family.ImageGenDailyLimit,
                boilerplate = family.ImageGenBoilerplate,
                model = family.ImageGenModel
            });
        });

        group.MapPut("/image-settings", async (
            ImageSettingsRequest req, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var family = await db.Families.FirstOrDefaultAsync(f => f.Id == user.FamilyId);
            if (family is null)
                return Results.NotFound(new { error = "Family not found" });

            if (req.DailyLimit is not null)
            {
                if (req.DailyLimit < ImageGen.MinDailyLimit || req.DailyLimit > ImageGen.MaxDailyLimit)
                    return Results.BadRequest(new { error = $"dailyLimit must be {ImageGen.MinDailyLimit}–{ImageGen.MaxDailyLimit}" });
                family.ImageGenDailyLimit = req.DailyLimit.Value;
            }

            if (req.Boilerplate is not null)
                family.ImageGenBoilerplate = req.Boilerplate.Trim();

            if (req.Model is not null)
            {
                if (string.IsNullOrWhiteSpace(req.Model))
                    return Results.BadRequest(new { error = "model required" });
                family.ImageGenModel = req.Model.Trim();
            }

            if (!string.IsNullOrWhiteSpace(req.OpenRouterApiKey))
                family.OpenRouterApiKey = req.OpenRouterApiKey.Trim();

            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                apiKeyMasked = ImageGen.MaskApiKey(family.OpenRouterApiKey),
                hasApiKey = !string.IsNullOrWhiteSpace(family.OpenRouterApiKey),
                dailyLimit = family.ImageGenDailyLimit,
                boilerplate = family.ImageGenBoilerplate,
                model = family.ImageGenModel
            });
        });

        return group;
    }

    public record MagicWordRequest(string MagicWord);
    public record OptionalActivityRequest(string Name, string? Url, string? Description, string? DefaultEffort);
    public record ImageSettingsRequest(string? OpenRouterApiKey, int? DailyLimit, string? Boilerplate, string? Model);
}
