using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Finance.Data;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Finance.Tests;

/// <summary>
/// Exercises the account query against a real SQLite database, which is the only way to catch the
/// class of bug where a [NotMapped] computed property is used inside a query EF must translate to
/// SQL. That threw at runtime ("Translation of member 'IsLiability' failed") and no amount of
/// in-memory object testing would have found it.
///
/// It also pins the asset/liability arithmetic: Plaid reports a credit card's balance as the amount
/// OWED, so it has to be subtracted from the net figure rather than added to it.
/// </summary>
public class AccountTotalsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aaronos-test-{Guid.NewGuid():N}.db");

    private AaronOsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AaronOsDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        IAppModule[] modules = [new FinanceModule()];
        return new AaronOsDbContext(options, modules);
    }

    [Fact]
    public async Task AccountQuery_TranslatesAndOrdersLiabilitiesLast()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.AddRange(
            new FinanceAccount { PlaidAccountId = "a1", Name = "Credit Card", Type = "credit", CurrentBalance = 555.45m },
            new FinanceAccount { PlaidAccountId = "a2", Name = "Checking", Type = "depository", Subtype = "checking", CurrentBalance = 4550.88m },
            new FinanceAccount { PlaidAccountId = "a3", Name = "Savings", Type = "depository", Subtype = "savings", CurrentBalance = 100m });
        await db.SaveChangesAsync();

        // Exactly the shape the dashboard uses: materialise, then sort on the computed property.
        var accounts = (await db.Set<FinanceAccount>().ToListAsync())
            .OrderBy(a => a.IsLiability)
            .ThenByDescending(a => a.CurrentBalance ?? 0)
            .ToList();

        Assert.Equal(["Checking", "Savings", "Credit Card"], accounts.Select(a => a.Name));

        var assets = accounts.Where(a => !a.IsLiability).Sum(a => a.CurrentBalance ?? 0);
        var liabilities = accounts.Where(a => a.IsLiability).Sum(a => a.CurrentBalance ?? 0);

        Assert.Equal(4650.88m, assets);
        Assert.Equal(555.45m, liabilities);
        // The bug this guards: naively summing every account reported 5206.33 as "total balance",
        // counting money owed as money held.
        Assert.Equal(4095.43m, assets - liabilities);
    }

    [Fact]
    public void LiabilityBalance_IsSignedNegativeForDisplay()
    {
        var credit = new FinanceAccount { Type = "credit", CurrentBalance = 555.45m };
        var checking = new FinanceAccount { Type = "depository", CurrentBalance = 4550.88m };

        Assert.Equal(-555.45m, credit.SignedBalance);
        Assert.Equal(4550.88m, checking.SignedBalance);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
