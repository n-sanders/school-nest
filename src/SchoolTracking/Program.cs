using Microsoft.EntityFrameworkCore;
using SchoolTracking.Data;
using SchoolTracking.Endpoints;
using SchoolTracking.Services;

var builder = WebApplication.CreateBuilder(args);

var configuredPath = builder.Configuration["Database:Path"];
var dbPath = string.IsNullOrWhiteSpace(configuredPath)
    ? Path.Combine(builder.Environment.ContentRootPath, "storage", "school.db")
    : configuredPath;
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<DeferralService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await SeedData.InitializeAsync(db);
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapAuthEndpoints();
app.MapFamilyEndpoints();
app.MapCatalogEndpoints();
app.MapPlannerEndpoints();
app.MapAssignmentEndpoints();
app.MapReportEndpoints();

app.Run();
