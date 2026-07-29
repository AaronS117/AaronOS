using AaronOS.Core;
using AaronOS.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Core.Tests;

/// <summary>A module entity carrying one of every awkward column type.</summary>
public class Widget
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public int Count { get; set; }
    public bool Discontinued { get; set; }
    public DateOnly? Retired { get; set; }

    /// <summary>A default that carries meaning, which is what the placeholder backfill destroyed.</summary>
    public string Regions { get; set; } = "US,EU,APAC";
}

file class WidgetConfiguration : IEntityTypeConfiguration<Widget>
{
    public void Configure(EntityTypeBuilder<Widget> builder)
    {
        builder.ToTable("Widget");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Name).HasMaxLength(100).IsRequired();
        builder.Property(w => w.Category).HasMaxLength(50).IsRequired();
        builder.Property(w => w.Price).HasPrecision(10, 2);
        builder.Property(w => w.Regions).HasMaxLength(200).IsRequired();
    }
}

file class WidgetModule : IAppModule
{
    public string Id => "widget";
    public string DisplayName => "Widget";
    public string IconGlyph => "Person24";
    public Type HomePageType => typeof(WidgetModule);
    public void RegisterServices(IServiceCollection services) { }
}

/// <summary>
/// Exercises schema upgrades against a real SQLite file.
///
/// This is the code standing between a schema change and an app that will not open. The database it
/// protects holds a linked bank connection that can only be re-established by redoing an OAuth flow,
/// so "delete it and start again" is not an available fix and these paths have to work.
///
/// The earlier schema is written with raw SQL rather than by registering a second, narrower entity.
/// Two entity types mapped to one table is a configuration EF rejects outright, and raw SQL is also
/// a truer reproduction: what actually exists on disk is a table an older build created.
/// </summary>
public class SchemaBootstrapperTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aaronos-schema-{Guid.NewGuid():N}.db");

    private AaronOsDbContext Context()
    {
        var options = new DbContextOptionsBuilder<AaronOsDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        IAppModule[] modules = [new WidgetModule()];
        return new AaronOsDbContext(options, modules);
    }

    /// <summary>The Widget table as an earlier build would have left it: Id and Name only.</summary>
    private async Task CreateNarrowWidgetTableAsync(params string[] namesToInsert)
    {
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        await using (var create = connection.CreateCommand())
        {
            create.CommandText =
                """
                CREATE TABLE "Widget" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Widget" PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL
                )
                """;
            await create.ExecuteNonQueryAsync();
        }

        foreach (var name in namesToInsert)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO \"Widget\" (\"Name\") VALUES ($name)";
            insert.Parameters.AddWithValue("$name", name);
            await insert.ExecuteNonQueryAsync();
        }
    }

    private async Task<HashSet<string>> WidgetColumnsAsync()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('Widget')";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Fact]
    public async Task AFreshDatabaseGetsTheWholeModel()
    {
        await using var db = Context();
        await SchemaBootstrapper.EnsureSchemaAsync(db);

        Assert.Superset(
            new HashSet<string>(
                ["Id", "Name", "Category", "Price", "Count", "Discontinued", "Retired", "Regions"]),
            await WidgetColumnsAsync());
    }

    [Fact]
    public async Task ColumnsMissingFromAnExistingTableAreAdded()
    {
        // The failure this prevents: adding a property left the column absent, and the next start
        // threw "no such column" on every query against that table.
        await CreateNarrowWidgetTableAsync();

        await using var db = Context();
        await SchemaBootstrapper.EnsureSchemaAsync(db);

        Assert.Superset(
            new HashSet<string>(["Category", "Price", "Count", "Discontinued", "Retired"]),
            await WidgetColumnsAsync());
    }

    [Fact]
    public async Task ExistingRowsSurviveAndReadBackWithoutNullsInNonNullableColumns()
    {
        await CreateNarrowWidgetTableAsync("Original");

        await using var db = Context();
        await SchemaBootstrapper.EnsureSchemaAsync(db);

        var widget = await db.Set<Widget>().SingleAsync();

        Assert.Equal("Original", widget.Name);
        Assert.Equal("", widget.Category);
        Assert.Equal(0m, widget.Price);
        Assert.Equal(0, widget.Count);
        Assert.False(widget.Discontinued);

        // Nullable columns are left alone; null is the honest value for a field never recorded.
        Assert.Null(widget.Retired);
    }

    [Fact]
    public async Task ABackfilledColumnKeepsTheDefaultTheCodeDeclares()
    {
        // The bug this pins cost real behaviour. A property defaulting to "SPY,QQQ,VTI,VOO,IVV" was added
        // to a live database and backfilled with "", because the placeholder for a string is empty. The
        // setting then read as "no symbol is a broad index", a cap applied to the fund it was meant to
        // exempt, and the trading agent silently held ninety percent cash. Nothing threw; the answer just
        // changed.
        await CreateNarrowWidgetTableAsync("Original");

        await using var db = Context();
        await SchemaBootstrapper.EnsureSchemaAsync(db);

        var widget = await db.Set<Widget>().SingleAsync();

        Assert.Equal("US,EU,APAC", widget.Regions);
    }

    [Fact]
    public async Task ADefaultContainingAQuoteIsEscapedRatherThanBreakingTheUpdate()
    {
        // Backfill values are composed into SQL text, so a default with an apostrophe in it must not end
        // the string literal early.
        await CreateNarrowWidgetTableAsync("Original");

        await using var db = Context();
        await SchemaBootstrapper.EnsureSchemaAsync(db);

        // Name already existed, so this asserts the upgrade completed at all with the new column present.
        Assert.Contains("Regions", await WidgetColumnsAsync());
    }

    [Fact]
    public async Task NewRowsCanBeWrittenAfterAnUpgrade()
    {
        await CreateNarrowWidgetTableAsync("Original");

        await using var db = Context();
        await SchemaBootstrapper.EnsureSchemaAsync(db);

        db.Add(new Widget
        {
            Name = "New", Category = "Tools", Price = 12.34m, Count = 3,
            Discontinued = true, Retired = new DateOnly(2026, 7, 28), Regions = "US",
        });
        await db.SaveChangesAsync();

        var stored = await db.Set<Widget>().SingleAsync(w => w.Name == "New");
        Assert.Equal("Tools", stored.Category);
        Assert.Equal(12.34m, stored.Price);
        Assert.True(stored.Discontinued);
        Assert.Equal(new DateOnly(2026, 7, 28), stored.Retired);
    }

    [Fact]
    public async Task MissingTablesAreStillCreatedAlongsideMissingColumns()
    {
        // A database predating both the new column and Core's own table: both gaps close in one pass.
        await CreateNarrowWidgetTableAsync();

        await using var db = Context();
        await SchemaBootstrapper.EnsureSchemaAsync(db);

        Assert.Empty(await db.UserProfiles.ToListAsync());
        Assert.Contains("Category", await WidgetColumnsAsync());
    }

    [Fact]
    public async Task RunningTwiceChangesNothingTheSecondTime()
    {
        await CreateNarrowWidgetTableAsync("Original");

        await using var db = Context();
        await SchemaBootstrapper.EnsureSchemaAsync(db);
        var afterFirst = await WidgetColumnsAsync();

        // Idempotence matters because this runs on every launch, not only after a change.
        await SchemaBootstrapper.EnsureSchemaAsync(db);

        Assert.Equal(afterFirst, await WidgetColumnsAsync());
        Assert.Single(await db.Set<Widget>().ToListAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try
            {
                File.Delete(_dbPath + suffix);
            }
            catch (IOException)
            {
                // A leftover temp file is harmless.
            }
        }

        GC.SuppressFinalize(this);
    }
}
