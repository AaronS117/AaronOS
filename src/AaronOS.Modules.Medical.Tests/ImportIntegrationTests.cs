using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Medical.Data;
using AaronOS.Modules.Medical.Tests.Fixtures;
using AaronOS.Modules.Medical.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Medical.Tests;

/// <summary>
/// End-to-end cover for the one code path that writes to the user's medical history. The parser and
/// planner are unit-tested in isolation; this drives the real ViewModel against a real SQLite file so
/// the parse → plan → commit → re-plan cycle is proven together, including that importing the same
/// document twice adds nothing the second time.
/// </summary>
public class ImportIntegrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aaronos-medical-{Guid.NewGuid():N}.db");
    private readonly string _xmlPath = Path.Combine(Path.GetTempPath(), $"aaronos-ccda-{Guid.NewGuid():N}.xml");
    private readonly TestContextFactory _factory;

    public ImportIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<AaronOsDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _factory = new TestContextFactory(options);

        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();

        File.WriteAllText(_xmlPath, CcdaFixtures.Document(
            CcdaFixtures.ProblemsSection,
            CcdaFixtures.MedicationsSection,
            CcdaFixtures.AllergiesSection,
            CcdaFixtures.ImmunizationsSection,
            CcdaFixtures.ResultsSection,
            CcdaFixtures.ProceduresSection,
            CcdaFixtures.EncountersSection,
            CcdaFixtures.VitalSignsSection));
    }

    private sealed class TestContextFactory(DbContextOptions<AaronOsDbContext> options)
        : IDbContextFactory<AaronOsDbContext>
    {
        // The context discovers entity configurations from each registered module's assembly, so the
        // real MedicalModule is what makes the Medical tables part of the model here too.
        private static readonly IAppModule[] Modules = [new MedicalModule()];

        public AaronOsDbContext CreateDbContext() => new(options, Modules);
    }

    private ImportViewModel NewViewModel()
    {
        var vm = new ImportViewModel(_factory);
        vm.SetFile(_xmlPath);
        return vm;
    }

    [Fact]
    public async Task ParseThenCommit_WritesEveryRecordTypeAndStampsThemImported()
    {
        var vm = NewViewModel();

        await vm.ParseCommand.ExecuteAsync(null);
        Assert.False(vm.HasError, vm.ErrorMessage);
        Assert.True(vm.NewCount > 0);
        var expected = vm.NewCount;

        await vm.CommitCommand.ExecuteAsync(null);

        await using var db = _factory.CreateDbContext();
        Assert.Equal(2, await db.Set<MedicalCondition>().CountAsync());
        Assert.Equal(1, await db.Set<Medication>().CountAsync());
        Assert.Equal(1, await db.Set<Allergy>().CountAsync());
        Assert.Equal(1, await db.Set<Immunization>().CountAsync());
        Assert.Equal(1, await db.Set<MedicalProcedure>().CountAsync());
        Assert.Equal(1, await db.Set<MedicalVisit>().CountAsync());
        Assert.Equal(4, await db.Set<LabResult>().CountAsync()); // 2 results + systolic + heart rate

        var total = await db.Set<MedicalCondition>().CountAsync()
            + await db.Set<Medication>().CountAsync()
            + await db.Set<Allergy>().CountAsync()
            + await db.Set<Immunization>().CountAsync()
            + await db.Set<MedicalProcedure>().CountAsync()
            + await db.Set<MedicalVisit>().CountAsync()
            + await db.Set<LabResult>().CountAsync();
        Assert.Equal(expected, total);

        // Provenance: everything written by an import must be distinguishable from hand entry.
        Assert.All(await db.Set<MedicalCondition>().ToListAsync(), c => Assert.True(c.IsImported));
        Assert.All(await db.Set<LabResult>().ToListAsync(), l => Assert.True(l.IsImported));
    }

    [Fact]
    public async Task ImportingTheSameDocumentTwice_AddsNothingTheSecondTime()
    {
        var first = NewViewModel();
        await first.ParseCommand.ExecuteAsync(null);
        await first.CommitCommand.ExecuteAsync(null);

        int CountAll(AaronOsDbContext db) =>
            db.Set<MedicalCondition>().Count() + db.Set<Medication>().Count()
            + db.Set<Allergy>().Count() + db.Set<Immunization>().Count()
            + db.Set<MedicalProcedure>().Count() + db.Set<MedicalVisit>().Count()
            + db.Set<LabResult>().Count();

        int afterFirst;
        await using (var db = _factory.CreateDbContext())
        {
            afterFirst = CountAll(db);
        }

        // The commit re-plans against what it just wrote, so the same screen should already report
        // nothing new — this is what stops a second click duplicating a history.
        Assert.Equal(0, first.NewCount);
        Assert.True(first.AlreadyImportedCount > 0);

        var second = NewViewModel();
        await second.ParseCommand.ExecuteAsync(null);
        Assert.Equal(0, second.NewCount);
        Assert.False(second.CanCommit);

        await second.CommitCommand.ExecuteAsync(null);

        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(afterFirst, CountAll(db));
        }
    }

    [Fact]
    public async Task BodyWeightIsNeverImported_BecauseBodyMeasurementsOwnsIt()
    {
        var vm = NewViewModel();
        await vm.ParseCommand.ExecuteAsync(null);
        await vm.CommitCommand.ExecuteAsync(null);

        await using var db = _factory.CreateDbContext();
        var names = await db.Set<LabResult>().Select(l => l.TestName).ToListAsync();

        Assert.DoesNotContain(names, n => n.Contains("weight", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.Contains("Systolic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportedLabKeepsItsReferenceRange_SoItCanBeFlagged()
    {
        var vm = NewViewModel();
        await vm.ParseCommand.ExecuteAsync(null);
        await vm.CommitCommand.ExecuteAsync(null);

        await using var db = _factory.CreateDbContext();
        var hb = await db.Set<LabResult>().SingleAsync(l => l.TestName == "Hemoglobin");

        Assert.Equal(14.2m, hb.Value);
        Assert.Equal(13.5m, hb.ReferenceLow);
        Assert.Equal(17.5m, hb.ReferenceHigh);
        Assert.False(hb.IsOutOfRange);   // 14.2 sits inside 13.5–17.5
    }

    [Fact]
    public async Task AResolvedConditionArrivesResolved()
    {
        var vm = NewViewModel();
        await vm.ParseCommand.ExecuteAsync(null);
        await vm.CommitCommand.ExecuteAsync(null);

        await using var db = _factory.CreateDbContext();
        var resolved = await db.Set<MedicalCondition>().SingleAsync(c => c.ExternalId == "cond-2");

        Assert.Equal(ConditionStatus.Resolved, resolved.Status);
        Assert.False(resolved.IsActive);
        Assert.Equal(new DateOnly(2019, 6, 1), resolved.ResolvedDate);
    }

    [Fact]
    public async Task AllergySeverityTextIsMappedOntoTheEnum()
    {
        var vm = NewViewModel();
        await vm.ParseCommand.ExecuteAsync(null);
        await vm.CommitCommand.ExecuteAsync(null);

        await using var db = _factory.CreateDbContext();
        var allergy = await db.Set<Allergy>().SingleAsync();

        Assert.Equal("Penicillin G", allergy.Substance);
        Assert.Equal(AllergySeverity.Moderate, allergy.Severity);
    }

    [Fact]
    public async Task AFileThatIsNotACcda_ReportsAnErrorAndWritesNothing()
    {
        var junk = Path.Combine(Path.GetTempPath(), $"aaronos-junk-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(junk, "<html><body>not a record</body></html>");
        try
        {
            var vm = new ImportViewModel(_factory);
            vm.SetFile(junk);

            await vm.ParseCommand.ExecuteAsync(null);

            Assert.True(vm.HasError);
            Assert.False(vm.CanCommit);
            Assert.Empty(vm.Rows);

            await using var db = _factory.CreateDbContext();
            Assert.Equal(0, await db.Set<MedicalCondition>().CountAsync());
        }
        finally
        {
            File.Delete(junk);
        }
    }

    public void Dispose()
    {
        // SQLite keeps a pooled connection open, so the handle must be released before the file can go.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _xmlPath })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test over.
            }
        }
    }
}
