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
builder.Services.AddHttpClient<OpenRouterImageService>(client =>
{
    client.BaseAddress = new Uri("https://openrouter.ai/");
    client.Timeout = TimeSpan.FromSeconds(ImageGen.GenerateTimeoutSeconds);
});

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
app.MapBackgroundEndpoints();
app.MapCatalogEndpoints();
app.MapPlannerEndpoints();
app.MapAssignmentEndpoints();
app.MapReportEndpoints();
app.MapCorrectionEndpoints();

app.Run();
