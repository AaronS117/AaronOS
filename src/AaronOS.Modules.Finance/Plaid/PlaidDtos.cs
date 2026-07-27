using System.Text.Json.Serialization;

namespace AaronOS.Modules.Finance.Plaid;

// ponytail: plain request/response DTOs matching Plaid's JSON field names via JsonPropertyName,
// rather than a full client SDK — this app calls four endpoints, not the whole Plaid surface.

public record PlaidLinkTokenResponse(
    [property: JsonPropertyName("link_token")] string LinkToken,
    [property: JsonPropertyName("expiration")] string Expiration);

public record PlaidExchangeTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("item_id")] string ItemId);

public record PlaidAccountDto(
    [property: JsonPropertyName("account_id")] string AccountId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("mask")] string? Mask,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("subtype")] string? Subtype,
    [property: JsonPropertyName("balances")] PlaidBalancesDto Balances);

public record PlaidBalancesDto(
    [property: JsonPropertyName("current")] decimal? Current,
    [property: JsonPropertyName("available")] decimal? Available,
    [property: JsonPropertyName("iso_currency_code")] string? IsoCurrencyCode);

public record PlaidItemDto(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("institution_id")] string? InstitutionId);

public record PlaidAccountsGetResponse(
    [property: JsonPropertyName("accounts")] List<PlaidAccountDto> Accounts,
    [property: JsonPropertyName("item")] PlaidItemDto Item);

public record PlaidInstitutionDto(
    [property: JsonPropertyName("institution_id")] string InstitutionId,
    [property: JsonPropertyName("name")] string Name);

public record PlaidInstitutionGetByIdResponse(
    [property: JsonPropertyName("institution")] PlaidInstitutionDto Institution);

public record PlaidPersonalFinanceCategoryDto(
    [property: JsonPropertyName("primary")] string? Primary,
    [property: JsonPropertyName("detailed")] string? Detailed);

public record PlaidTransactionDto(
    [property: JsonPropertyName("transaction_id")] string TransactionId,
    [property: JsonPropertyName("account_id")] string AccountId,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("pending")] bool Pending,
    [property: JsonPropertyName("iso_currency_code")] string? IsoCurrencyCode,
    [property: JsonPropertyName("personal_finance_category")] PlaidPersonalFinanceCategoryDto? PersonalFinanceCategory);

public record PlaidRemovedTransactionDto(
    [property: JsonPropertyName("transaction_id")] string TransactionId);

public record PlaidTransactionsSyncResponse(
    [property: JsonPropertyName("added")] List<PlaidTransactionDto> Added,
    [property: JsonPropertyName("modified")] List<PlaidTransactionDto> Modified,
    [property: JsonPropertyName("removed")] List<PlaidRemovedTransactionDto> Removed,
    [property: JsonPropertyName("next_cursor")] string NextCursor,
    [property: JsonPropertyName("has_more")] bool HasMore);

/// <summary>Accumulated result of looping transactions/sync until has_more is false.</summary>
public record PlaidSyncResult(
    List<PlaidTransactionDto> Added,
    List<PlaidTransactionDto> Modified,
    List<string> RemovedIds,
    string NextCursor);
