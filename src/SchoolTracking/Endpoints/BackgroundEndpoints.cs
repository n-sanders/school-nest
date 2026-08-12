using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Models;
using SchoolTracking.Services;

namespace SchoolTracking.Endpoints;

public static class BackgroundEndpoints
{
    public static RouteGroupBuilder MapBackgroundEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/backgrounds");

        group.MapGet("", async (AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireUserAsync(auth);
            if (user is null) return Results.Empty;

            if (user.IsParent)
            {
                var rows = await db.GeneratedBackgrounds
                    .Where(b => b.FamilyId == user.FamilyId)
                    .Join(db.Users, b => b.StudentUserId, u => u.Id, (b, u) => new
                    {
                        b.Id,
                        b.StudentUserId,
                        studentName = u.DisplayName,
                        b.StudentPrompt,
                        b.CreatedAt
                    })
                    .OrderBy(b => b.studentName)
                    .ThenByDescending(b => b.CreatedAt)
                    .ToListAsync();

                var rejectionRows = await db.RejectedImagePrompts
                    .Where(r => r.FamilyId == user.FamilyId)
                    .Join(db.Users, r => r.StudentUserId, u => u.Id, (r, u) => new
                    {
                        r.Id,
                        r.StudentUserId,
                        studentName = u.DisplayName,
                        r.StudentPrompt,
                        r.ProviderMessage,
                        r.CreatedAt
                    })
                    .OrderBy(r => r.studentName)
                    .ThenByDescending(r => r.CreatedAt)
                    .ToListAsync();

                return Results.Ok(new
                {
                    images = rows.Select(b => new
                    {
                        b.Id,
                        b.StudentUserId,
                        b.studentName,
                        b.StudentPrompt,
                        createdAt = b.CreatedAt,
                        imageUrl = ImageGen.ImageUrl(b.Id)
                    }),
                    rejections = rejectionRows.Select(r => new
                    {
                        r.Id,
                        r.StudentUserId,
                        r.studentName,
                        r.StudentPrompt,
                        r.ProviderMessage,
                        createdAt = r.CreatedAt
                    })
                });
            }

            if (!user.IsStudent)
                return Results.Forbid();

            var family = await db.Families.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == user.FamilyId);
            if (family is null)
                return Results.NotFound(new { error = "Family not found" });

            var (startUtc, endUtc) = ImageGen.TodayUtcRange();
            var usedToday = await db.GeneratedBackgrounds.CountAsync(b =>
                b.StudentUserId == user.Id && b.CreatedAt >= startUtc && b.CreatedAt < endUtc);

            var images = await db.GeneratedBackgrounds
                .Where(b => b.StudentUserId == user.Id)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new { b.Id, b.StudentPrompt, b.CreatedAt })
                .ToListAsync();

            var remaining = Math.Max(0, family.ImageGenDailyLimit - usedToday);
            return Results.Ok(new
            {
                configured = !string.IsNullOrWhiteSpace(family.OpenRouterApiKey),
                dailyLimit = family.ImageGenDailyLimit,
                usedToday,
                remainingToday = remaining,
                generateTimeoutSeconds = ImageGen.GenerateTimeoutSeconds,
                activeBackgroundId = user.ActiveBackgroundId,
                images = images.Select(b => new
                {
                    b.Id,
                    b.StudentPrompt,
                    createdAt = b.CreatedAt,
                    imageUrl = ImageGen.ImageUrl(b.Id)
                })
            });
        });

        group.MapPost("", async (
            GenerateBackgroundRequest req,
            AuthService auth,
            HttpContext http,
            AppDbContext db,
            OpenRouterImageService images,
            CancellationToken cancellationToken) =>
        {
            var user = await http.RequireStudentAsync(auth);
            if (user is null) return Results.Empty;

            var prompt = ImageGen.NormalizeStudentPrompt(req.Prompt ?? "");
            if (prompt.Length == 0)
                return Results.BadRequest(new { error = "Describe the background you want" });

            var family = await db.Families.FirstOrDefaultAsync(f => f.Id == user.FamilyId, cancellationToken);
            if (family is null)
                return Results.NotFound(new { error = "Family not found" });
            if (string.IsNullOrWhiteSpace(family.OpenRouterApiKey))
                return Results.BadRequest(new { error = "Ask a parent to turn this on" });

            var (startUtc, endUtc) = ImageGen.TodayUtcRange();
            var usedToday = await db.GeneratedBackgrounds.CountAsync(b =>
                b.StudentUserId == user.Id && b.CreatedAt >= startUtc && b.CreatedAt < endUtc, cancellationToken);
            if (usedToday >= family.ImageGenDailyLimit)
            {
                return Results.Json(
                    new { error = $"Daily limit reached ({family.ImageGenDailyLimit}). Try again tomorrow." },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var combined = ImageGen.CombinePrompt(family.ImageGenBoilerplate, prompt);
            var model = string.IsNullOrWhiteSpace(family.ImageGenModel)
                ? ImageGen.DefaultModel
                : family.ImageGenModel.Trim();

            byte[] bytes;
            string contentType;
            try
            {
                (bytes, contentType) = await images.GenerateAsync(
                    family.OpenRouterApiKey, model, combined, cancellationToken);
            }
            catch (OpenRouterImageException ex)
            {
                if (ex.IsModeration)
                {
                    db.RejectedImagePrompts.Add(new RejectedImagePrompt
                    {
                        FamilyId = user.FamilyId,
                        StudentUserId = user.Id,
                        StudentPrompt = prompt,
                        ProviderMessage = string.IsNullOrWhiteSpace(ex.Message) ? null : ex.Message,
                        CreatedAt = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync(cancellationToken);
                    return Results.BadRequest(new { error = "That description wasn't allowed. Try a different one." });
                }

                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
            catch (TaskCanceledException)
            {
                return Results.Json(new { error = "Image generation timed out. Try again." }, statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (HttpRequestException)
            {
                return Results.Json(new { error = "Could not reach the image service. Try again." }, statusCode: StatusCodes.Status502BadGateway);
            }

            var item = new GeneratedBackground
            {
                FamilyId = user.FamilyId,
                StudentUserId = user.Id,
                StudentPrompt = prompt,
                ImageBytes = bytes,
                ContentType = contentType,
                CreatedAt = DateTime.UtcNow
            };
            db.GeneratedBackgrounds.Add(item);
            await db.SaveChangesAsync(cancellationToken);

            var student = await db.Users.FirstAsync(u => u.Id == user.Id, cancellationToken);
            student.ActiveBackgroundId = item.Id;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new
            {
                item.Id,
                item.StudentPrompt,
                createdAt = item.CreatedAt,
                imageUrl = ImageGen.ImageUrl(item.Id),
                activeBackgroundId = item.Id
            });
        });

        group.MapPut("/active", async (
            SetActiveBackgroundRequest req, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireStudentAsync(auth);
            if (user is null) return Results.Empty;

            var student = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
            if (student is null)
                return Results.NotFound(new { error = "User not found" });

            if (req.Id is null)
            {
                student.ActiveBackgroundId = null;
                await db.SaveChangesAsync();
                return Results.Ok(new { activeBackgroundId = (int?)null });
            }

            var exists = await db.GeneratedBackgrounds.AnyAsync(b =>
                b.Id == req.Id && b.StudentUserId == user.Id);
            if (!exists)
                return Results.NotFound(new { error = "Image not found" });

            student.ActiveBackgroundId = req.Id;
            await db.SaveChangesAsync();
            return Results.Ok(new { activeBackgroundId = req.Id });
        });

        group.MapDelete("/rejections/{id:int}", async (int id, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var item = await db.RejectedImagePrompts.FirstOrDefaultAsync(r =>
                r.Id == id && r.FamilyId == user.FamilyId);
            if (item is null)
                return Results.NotFound(new { error = "Entry not found" });

            db.RejectedImagePrompts.Remove(item);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        group.MapDelete("/{id:int}", async (int id, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireParentAsync(auth);
            if (user is null) return Results.Empty;

            var item = await db.GeneratedBackgrounds.FirstOrDefaultAsync(b =>
                b.Id == id && b.FamilyId == user.FamilyId);
            if (item is null)
                return Results.NotFound(new { error = "Image not found" });

            var holders = await db.Users.Where(u => u.ActiveBackgroundId == id).ToListAsync();
            foreach (var holder in holders)
                holder.ActiveBackgroundId = null;

            db.GeneratedBackgrounds.Remove(item);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        group.MapGet("/{id:int}/image", async (int id, AuthService auth, HttpContext http, AppDbContext db) =>
        {
            var user = await http.RequireUserAsync(auth);
            if (user is null) return Results.Empty;

            var item = await db.GeneratedBackgrounds.AsNoTracking()
                .Where(b => b.Id == id)
                .Select(b => new { b.FamilyId, b.StudentUserId, b.ImageBytes, b.ContentType })
                .FirstOrDefaultAsync();
            if (item is null)
                return Results.NotFound();

            var allowed = user.IsParent
                ? item.FamilyId == user.FamilyId
                : item.StudentUserId == user.Id;
            if (!allowed)
                return Results.NotFound();

            http.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.File(item.ImageBytes, item.ContentType);
        });

        return group;
    }

    public record GenerateBackgroundRequest(string? Prompt);
    public record SetActiveBackgroundRequest(int? Id);
}
