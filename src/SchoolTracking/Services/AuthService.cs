using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Models;

namespace SchoolTracking.Services;

public class CurrentUser
{
    public int Id { get; init; }
    public int FamilyId { get; init; }
    public string DisplayName { get; init; } = "";
    public UserRole Role { get; init; }

    public bool IsParent => Role == UserRole.Parent;
    public bool IsStudent => Role == UserRole.Student;
}

public static class AuthConstants
{
    public const string CookieName = "st_session";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
}

public class AuthService(AppDbContext db)
{
    public async Task<(Session session, User user)?> LoginAsync(int userId, string magicWord)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user is null || !string.Equals(user.MagicWord, magicWord, StringComparison.Ordinal))
            return null;

        var session = new Session
        {
            UserId = user.Id,
            Token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            ExpiresAt = DateTime.UtcNow.Add(AuthConstants.SessionLifetime)
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return (session, user);
    }

    public async Task LogoutAsync(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return;

        var sessions = await db.Sessions.Where(s => s.Token == token).ToListAsync();
        db.Sessions.RemoveRange(sessions);
        await db.SaveChangesAsync();
    }

    public async Task<CurrentUser?> GetCurrentUserAsync(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        var session = await db.Sessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Token == token && s.ExpiresAt > DateTime.UtcNow);

        if (session is null || !session.User.IsActive)
            return null;

        return new CurrentUser
        {
            Id = session.User.Id,
            FamilyId = session.User.FamilyId,
            DisplayName = session.User.DisplayName,
            Role = session.User.Role
        };
    }
}

public static class HttpAuthExtensions
{
    public static string? GetSessionToken(this HttpRequest request) =>
        request.Cookies[AuthConstants.CookieName];

    public static void SetSessionCookie(this HttpResponse response, string token)
    {
        response.Cookies.Append(AuthConstants.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.Add(AuthConstants.SessionLifetime)
        });
    }

    public static void ClearSessionCookie(this HttpResponse response)
    {
        response.Cookies.Delete(AuthConstants.CookieName);
    }

    public static async Task<CurrentUser?> RequireUserAsync(this HttpContext http, AuthService auth)
    {
        var user = await auth.GetCurrentUserAsync(http.Request.GetSessionToken());
        if (user is null)
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await http.Response.WriteAsJsonAsync(new { error = "Not authenticated" });
        }
        return user;
    }

    public static async Task<CurrentUser?> RequireParentAsync(this HttpContext http, AuthService auth)
    {
        var user = await http.RequireUserAsync(auth);
        if (user is null)
            return null;
        if (!user.IsParent)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.Response.WriteAsJsonAsync(new { error = "Parents only" });
            return null;
        }
        return user;
    }
}
