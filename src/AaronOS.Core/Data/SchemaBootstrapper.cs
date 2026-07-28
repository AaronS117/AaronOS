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
/// Limitation, stated plainly: this adds tables and columns that do not exist yet. It does not
/// rename, retype or drop anything, and it does not reproduce a NOT NULL constraint on a column it
/// adds — those still need real migrations. It covers adding a module and adding a property, which
/// are the two cases the module architecture makes routine.
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

        if (missing.Count > 0)
        {
            // GenerateCreateScript emits the whole model; run only the statements belonging to
            // tables that are actually absent, so existing tables (and their data) are untouched.
            foreach (var statement in SplitStatements(db.Database.GenerateCreateScript()))
            {
                var target = TargetTableOf(statement);
                if (target is not null && missing.Contains(target))
                {
                    await db.Database.ExecuteSqlRawAsync(statement);
                }
            }
        }

        await AddMissingColumnsAsync(db, missing);
    }

    /// <summary>
    /// Adds columns that the model has and the table does not.
    ///
    /// This closes the other half of the gap. Adding a property to an existing module's entity used
    /// to leave the column absent and every query on that table threw "no such column" on the next
    /// start, with deleting the database as the only remedy — unacceptable when it holds a bank link
    /// that can only be re-established through an OAuth flow.
    ///
    /// Columns are added without NOT NULL even when the model declares it, because SQLite refuses a
    /// NOT NULL column on an existing table unless given a constant default, and inventing one per
    /// type is guesswork. Existing rows are then filled with a type-appropriate value so nothing
    /// reads back null into a non-nullable property. State the consequence plainly: a table upgraded
    /// this way is slightly laxer than the same table created fresh. It accepts a null that a new
    /// database would reject, which is a divergence rather than a corruption, and real migrations
    /// remain the answer for anything beyond adding a column.
    /// </summary>
    private static async Task AddMissingColumnsAsync(AaronOsDbContext db, HashSet<string> justCreated)
    {
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();

            // A table created a moment ago already matches the model exactly.
            if (string.IsNullOrEmpty(table) || justCreated.Contains(table))
            {
                continue;
            }

            var existingColumns = await GetExistingColumnNamesAsync(db, table);
            if (existingColumns.Count == 0)
            {
                // Not a real table (a view, or an entity mapped elsewhere) — nothing to alter.
                continue;
            }

            foreach (var property in entityType.GetProperties())
            {
                var column = property.GetColumnName();
                if (string.IsNullOrEmpty(column) || existingColumns.Contains(column))
                {
                    continue;
                }

                var columnType = property.GetColumnType() ?? "TEXT";

                // EF1002 is suppressed rather than worked around, because it cannot be worked around:
                // SQL has no parameter form for a table or column name, so DDL has to be composed as
                // text. What makes it safe is the provenance of the values — table name, column name
                // and store type all come from the compiled EF model, and the default literal comes
                // from a fixed switch over CLR types. None of it originates from user input or from
                // the database's own contents.
#pragma warning disable EF1002
                await db.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {columnType}");

                if (!property.IsNullable)
                {
                    await db.Database.ExecuteSqlRawAsync(
                        $"UPDATE \"{table}\" SET \"{column}\" = {DefaultLiteralFor(property.ClrType)} " +
                        $"WHERE \"{column}\" IS NULL");
                }
#pragma warning restore EF1002
            }
        }
    }

    /// <summary>
    /// A SQL literal safe to backfill a newly added non-nullable column with.
    ///
    /// These are placeholders, not meaningful values: a column that did not exist has no history, so
    /// the only requirement is that the stored text parses back into the property's type. Dates get
    /// the minimum value rather than today's, so a backfilled row is recognisable as one that was
    /// never actually recorded.
    /// </summary>
    private static string DefaultLiteralFor(Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (type == typeof(bool))
        {
            return "0";
        }

        if (type.IsEnum)
        {
            // Enums are stored as text throughout this app (see the module configurations).
            return $"'{Enum.GetNames(type).FirstOrDefault() ?? ""}'";
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return "'0001-01-01 00:00:00'";
        }

        if (type == typeof(DateOnly))
        {
            return "'0001-01-01'";
        }

        if (type == typeof(TimeOnly) || type == typeof(TimeSpan))
        {
            return "'00:00:00'";
        }

        if (type == typeof(Guid))
        {
            return $"'{Guid.Empty}'";
        }

        if (type == typeof(string))
        {
            return "''";
        }

        // Numerics. Decimal reaches SQLite as text, so quoting a zero is correct for both storage
        // shapes and parses back either way.
        return type == typeof(decimal) ? "'0'" : "0";
    }

    private static async Task<HashSet<string>> GetExistingColumnNamesAsync(AaronOsDbContext db, string table)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info($table)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$table";
        parameter.Value = table;
        command.Parameters.Add(parameter);

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
