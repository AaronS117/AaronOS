using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Core.Data;

/// <summary>
/// Creates the database on first run and, crucially, adds tables for modules registered *after*
/// the database already existed.
///
/// Why this exists: the app deliberately has no EF migrations (see docs/MODULE_GUIDELINES.md), and
/// <c>EnsureCreatedAsync()</c> alone is all-or-nothing — it creates the whole schema when the file
/// is absent and does nothing at all when it is present. So registering a new module against an
/// existing database left its tables missing and the app crashed on first query
/// ("no such table: X"). Deleting the database to fix that is not acceptable: it destroys real
/// data, including linked bank connections that can only be re-established by redoing an OAuth
/// flow. This closes that gap by creating just the missing tables.
///
/// Limitation, stated plainly: this adds tables that do not exist yet. It does not alter tables
/// whose columns changed — that still needs real migrations. It covers the "new module added"
/// case, which is the one the module architecture makes routine.
/// </summary>
public static class SchemaBootstrapper
{
    public static async Task EnsureSchemaAsync(AaronOsDbContext db)
    {
        var created = await db.Database.EnsureCreatedAsync();
        if (created)
        {
            // Fresh database: EnsureCreated already built the full current model.
            return;
        }

        var existing = await GetExistingTableNamesAsync(db);
        var modelTables = db.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missing = modelTables
            .Where(t => !existing.Contains(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (missing.Count == 0)
        {
            return;
        }

        // GenerateCreateScript emits the whole model; run only the statements belonging to tables
        // that are actually absent, so existing tables (and their data) are left untouched.
        foreach (var statement in SplitStatements(db.Database.GenerateCreateScript()))
        {
            var target = TargetTableOf(statement);
            if (target is not null && missing.Contains(target))
            {
                await db.Database.ExecuteSqlRawAsync(statement);
            }
        }
    }

    private static async Task<HashSet<string>> GetExistingTableNamesAsync(AaronOsDbContext db)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";

        await db.Database.OpenConnectionAsync();
        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return names;
    }

    private static IEnumerable<string> SplitStatements(string script) =>
        script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0);

    /// <summary>Table a CREATE TABLE / CREATE INDEX statement applies to, or null if neither.</summary>
    private static string? TargetTableOf(string statement)
    {
        var table = Regex.Match(statement, @"CREATE\s+TABLE\s+""(?<name>[^""]+)""", RegexOptions.IgnoreCase);
        if (table.Success)
        {
            return table.Groups["name"].Value;
        }

        var index = Regex.Match(statement, @"CREATE\s+(?:UNIQUE\s+)?INDEX\s+""[^""]+""\s+ON\s+""(?<name>[^""]+)""", RegexOptions.IgnoreCase);
        return index.Success ? index.Groups["name"].Value : null;
    }
}
