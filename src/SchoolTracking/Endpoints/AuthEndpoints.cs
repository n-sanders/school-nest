using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Models;
using SchoolTracking.Services;

namespace SchoolTracking.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapGet("/users", async (AppDbContext db) =>
        {
            var users = await db.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.Role)
                .ThenBy(u => u.DisplayName)
                .Select(u => new { u.Id, u.DisplayName, role = u.Role.ToString().ToLowerInvariant() })
                .ToListAsync();
            return Results.Ok(users);
        });

        group.MapPost("/login", async (LoginRequest req, AuthService auth, HttpContext http) =>
        {
            if (req.UserId <= 0 || string.IsNullOrWhiteSpace(req.MagicWord))
                return Results.BadRequest(new { error = "userId and magicWord required" });

            var result = await auth.LoginAsync(req.UserId, req.MagicWord);
            if (result is null)
                return Results.Unauthorized();

            var (session, user) = result.Value;
            http.Response.SetSessionCookie(session.Token);
            return Results.Ok(new
            {
                id = user.Id,
                displayName = user.DisplayName,
                role = user.Role.ToString().ToLowerInvariant(),
                familyId = user.FamilyId
            });
        });

        group.MapPost("/logout", async (AuthService auth, HttpContext http) =>
        {
            await auth.LogoutAsync(http.Request.GetSessionToken());
            http.Response.ClearSessionCookie();
            return Results.Ok(new { ok = true });
        });

        group.MapGet("/me", async (AuthService auth, HttpContext http) =>
        {
            var user = await auth.GetCurrentUserAsync(http.Request.GetSessionToken());
            if (user is null)
                return Results.Unauthorized();
            return Results.Ok(new
            {
                id = user.Id,
                displayName = user.DisplayName,
                role = user.Role.ToString().ToLowerInvariant(),
                familyId = user.FamilyId,
                activeBackgroundId = user.ActiveBackgroundId
            });
        });

        return group;
    }

    public record LoginRequest(int UserId, string MagicWord);
}
