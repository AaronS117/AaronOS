using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Finance.Data;
using AaronOS.Modules.Finance.Plaid;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Finance.ViewModels;

public partial class LinkAccountViewModel(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    PlaidApiClient plaidApiClient) : ViewModelBase
{
    [ObservableProperty]
    private string? _linkToken;

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>Raised once a bank connection has been saved, so the hosting page can navigate away.</summary>
    public event Action? AccountLinked;

    [RelayCommand]
    private async Task CreateLinkTokenAsync()
    {
        IsBusy = true;
        StatusMessage = "";
        try
        {
            LinkToken = await plaidApiClient.CreateLinkTokenAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not start Plaid Link: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Called by LinkAccountPage once the WebView2-hosted Plaid Link flow succeeds.
    /// institutionName comes from Plaid Link's own onSuccess metadata — no extra API call needed.</summary>
    public async Task CompleteLinkAsync(string publicToken, string institutionId, string institutionName)
    {
        IsBusy = true;
        StatusMessage = "";
        try
        {
            var exchange = await plaidApiClient.ExchangePublicTokenAsync(publicToken);
            var accountsResponse = await plaidApiClient.GetAccountsAsync(exchange.AccessToken);

            await using var db = await dbContextFactory.CreateDbContextAsync();
            var item = new PlaidItem
            {
                ItemId = exchange.ItemId,
                InstitutionId = institutionId,
                InstitutionName = institutionName,
                AccessTokenEncrypted = AccessTokenProtector.Protect(exchange.AccessToken),
                CreatedAt = DateTimeOffset.Now
            };
            db.Add(item);
            await db.SaveChangesAsync();

            foreach (var accountDto in accountsResponse.Accounts)
            {
                db.Add(new FinanceAccount
                {
                    PlaidAccountId = accountDto.AccountId,
                    PlaidItemId = item.Id,
                    Name = accountDto.Name,
                    Mask = accountDto.Mask,
                    Type = accountDto.Type,
                    Subtype = accountDto.Subtype,
                    CurrentBalance = accountDto.Balances.Current,
                    AvailableBalance = accountDto.Balances.Available,
                    IsoCurrencyCode = accountDto.Balances.IsoCurrencyCode ?? "USD"
                });
            }

            await db.SaveChangesAsync();

            StatusMessage = $"Linked {institutionName}.";
            AccountLinked?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Link failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
