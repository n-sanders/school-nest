using Microsoft.EntityFrameworkCore;
using SchoolTracking.Models;

namespace SchoolTracking.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Family> Families => Set<Family>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CatalogAssignment> CatalogAssignments => Set<CatalogAssignment>();
    public DbSet<OptionalActivity> OptionalActivities => Set<OptionalActivity>();
    public DbSet<PlannedDay> PlannedDays => Set<PlannedDay>();
    public DbSet<Assignment> Assignments => Set<Assignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Family>(e =>
        {
            e.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(x => new { x.FamilyId, x.DisplayName });
            e.HasOne(x => x.Family).WithMany(x => x.Users).HasForeignKey(x => x.FamilyId);
        });

        modelBuilder.Entity<Session>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne(x => x.User).WithMany(x => x.Sessions).HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<Subject>(e =>
        {
            e.HasIndex(x => new { x.FamilyId, x.SortOrder });
            e.HasOne(x => x.Family).WithMany(x => x.Subjects).HasForeignKey(x => x.FamilyId);
        });

        modelBuilder.Entity<Course>(e =>
        {
            e.HasIndex(x => new { x.SubjectId, x.SortOrder });
            e.HasOne(x => x.Subject).WithMany(x => x.Courses).HasForeignKey(x => x.SubjectId);
        });

        modelBuilder.Entity<CatalogAssignment>(e =>
        {
            e.HasIndex(x => new { x.CourseId, x.SortOrder });
            e.HasOne(x => x.Course).WithMany(x => x.CatalogAssignments).HasForeignKey(x => x.CourseId);
        });

        modelBuilder.Entity<OptionalActivity>(e =>
        {
            e.HasIndex(x => new { x.FamilyId, x.SortOrder });
            e.HasOne(x => x.Family).WithMany(x => x.OptionalActivities).HasForeignKey(x => x.FamilyId);
        });

        modelBuilder.Entity<PlannedDay>(e =>
        {
            e.HasIndex(x => new { x.StudentUserId, x.SequenceIndex }).IsUnique();
            e.HasOne(x => x.Student).WithMany(x => x.PlannedDays).HasForeignKey(x => x.StudentUserId);
        });

        modelBuilder.Entity<Assignment>(e =>
        {
            e.HasIndex(x => x.StudentUserId);
            e.HasIndex(x => x.PlannedDayId);
            e.HasIndex(x => x.ActivityDate);
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.Student).WithMany(x => x.Assignments).HasForeignKey(x => x.StudentUserId);
            e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).IsRequired(false);
            e.HasOne(x => x.CatalogAssignment).WithMany().HasForeignKey(x => x.CatalogAssignmentId);
            e.HasOne(x => x.OptionalActivity).WithMany().HasForeignKey(x => x.OptionalActivityId);
            e.HasOne(x => x.PlannedDay).WithMany(x => x.Assignments).HasForeignKey(x => x.PlannedDayId);
        });
    }
}
