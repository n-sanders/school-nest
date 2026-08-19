using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SchoolTracking.Services;

namespace SchoolTracking.Data;

/// <summary>
/// Additive SQLite patches for existing databases. EnsureCreated does not add
/// new tables or columns to a file that already exists.
/// </summary>
public static class SchemaPatches
{
    public static async Task ApplyAsync(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        var opened = conn.State != ConnectionState.Open;
        if (opened)
            await conn.OpenAsync();

        try
        {
            await AddColumnIfMissingAsync(conn, "Families", "OpenRouterApiKey", "TEXT");
            await AddColumnIfMissingAsync(
                conn, "Families", "ImageGenDailyLimit",
                $"INTEGER NOT NULL DEFAULT {ImageGen.DefaultDailyLimit}");
            await AddColumnIfMissingAsync(
                conn, "Families", "ImageGenBoilerplate",
                $"TEXT NOT NULL DEFAULT '{EscapeSqlLiteral(ImageGen.DefaultBoilerplate)}'");
            await AddColumnIfMissingAsync(
                conn, "Families", "ImageGenModel",
                $"TEXT NOT NULL DEFAULT '{EscapeSqlLiteral(ImageGen.DefaultModel)}'");

            await ExecuteAsync(conn, """
                CREATE TABLE IF NOT EXISTS GeneratedBackgrounds (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FamilyId INTEGER NOT NULL,
                    StudentUserId INTEGER NOT NULL,
                    StudentPrompt TEXT NOT NULL,
                    ImageBytes BLOB NOT NULL,
                    ContentType TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (FamilyId) REFERENCES Families (Id) ON DELETE CASCADE,
                    FOREIGN KEY (StudentUserId) REFERENCES Users (Id) ON DELETE CASCADE
                );
                """);
            await ExecuteAsync(conn,
                "CREATE INDEX IF NOT EXISTS IX_GeneratedBackgrounds_StudentUserId ON GeneratedBackgrounds (StudentUserId);");
            await ExecuteAsync(conn,
                "CREATE INDEX IF NOT EXISTS IX_GeneratedBackgrounds_FamilyId ON GeneratedBackgrounds (FamilyId);");
            await ExecuteAsync(conn,
                "CREATE INDEX IF NOT EXISTS IX_GeneratedBackgrounds_CreatedAt ON GeneratedBackgrounds (CreatedAt);");

            await AddColumnIfMissingAsync(conn, "Users", "ActiveBackgroundId", "INTEGER");

            await ExecuteAsync(conn, """
                CREATE TABLE IF NOT EXISTS RejectedImagePrompts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FamilyId INTEGER NOT NULL,
                    StudentUserId INTEGER NOT NULL,
                    StudentPrompt TEXT NOT NULL,
                    ProviderMessage TEXT,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (FamilyId) REFERENCES Families (Id) ON DELETE CASCADE,
                    FOREIGN KEY (StudentUserId) REFERENCES Users (Id) ON DELETE CASCADE
                );
                """);
            await ExecuteAsync(conn,
                "CREATE INDEX IF NOT EXISTS IX_RejectedImagePrompts_FamilyId ON RejectedImagePrompts (FamilyId);");
            await ExecuteAsync(conn,
                "CREATE INDEX IF NOT EXISTS IX_RejectedImagePrompts_StudentUserId ON RejectedImagePrompts (StudentUserId);");
            await ExecuteAsync(conn,
                "CREATE INDEX IF NOT EXISTS IX_RejectedImagePrompts_CreatedAt ON RejectedImagePrompts (CreatedAt);");

            await AddColumnIfMissingAsync(conn, "PlannedDays", "StartedOn", "TEXT");
            await AddColumnIfMissingAsync(
                conn, "Assignments", "CarryoverKind", "INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync(conn, "Assignments", "SourcePlannedDayId", "INTEGER");
        }
        finally
        {
            if (opened)
                await conn.CloseAsync();
        }
    }

    private static async Task AddColumnIfMissingAsync(
        DbConnection conn, string table, string column, string typeSql)
    {
        if (await ColumnExistsAsync(conn, table, column))
            return;
        await ExecuteAsync(conn, $"ALTER TABLE {table} ADD COLUMN {column} {typeSql}");
    }

    private static async Task<bool> ColumnExistsAsync(DbConnection conn, string table, string column)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = $column";
        AddParam(cmd, "$column", column);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task ExecuteAsync(DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddParam(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
