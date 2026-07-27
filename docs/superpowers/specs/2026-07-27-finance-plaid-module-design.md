# Finance Module (Plaid) — Design

## Context

AaronOS is a modular WPF desktop app (migrated from WinUI 3 — see
`2026-07-26-wpf-migration-design.md`/commit `77798fb`). It currently has one module,
`AaronOS.Modules.BodyMeasurements`. The user wants a second module that connects to their bank
accounts via Plaid, syncs balances and transactions, and shows spend broken down by category — a
personal finance watcher running only on their own machine.

This module follows `docs/MODULE_GUIDELINES.md` exactly: a compiled-in `IAppModule`, its own
entities discovered automatically by the shared `AaronOsDbContext`, its own ViewModels/Pages, one
project reference from `AaronOS.App`, one line in the module array.

## Scope for v1

- Link one or more bank accounts through Plaid Link.
- Manually sync balances and transactions (no background/scheduled sync).
- Dashboard: total balances, account list, recent transactions, spend-by-category chart for the
  current month (using Plaid's own Personal Finance Category taxonomy — no custom categorization
  UI).
- Full transaction list, filterable by account.

Explicitly out of scope for v1 (noted so it isn't silently forgotten, not because it's hard):
- Handling `ITEM_LOGIN_REQUIRED` re-auth ("update mode" Link) — if a bank connection breaks, the
  dashboard shows a "reconnect needed" banner with no working fix-it flow yet.
- User-defined budgets, progress bars, or over-budget alerts.
- Automatic/background sync.
- Sandbox/Development Plaid environments — this targets Production only, since the user already
  has Production keys. The base URL (`https://production.plaid.com`) is hardcoded rather than
  configurable; add an environment switch later if it's ever needed.

## Module shape

`AaronOS.Modules.Finance`, a class library exactly like `BodyMeasurements`:

```csharp
public class FinanceModule : IAppModule
{
    public string Id => "finance";
    public string DisplayName => "Finance";
    public string IconGlyph => "Wallet24"; // confirm exact Wpf.Ui.Controls.SymbolRegular member at implementation time
    public Type HomePageType => typeof(FinanceShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<PlaidApiClient>();
        services.AddSingleton<PlaidCredentialStore>();
        services.AddTransient<FinanceDashboardViewModel>();
        services.AddTransient<FinanceTransactionsViewModel>();
        services.AddTransient<LinkAccountViewModel>();
    }
}
```

`AaronOS.App` gets a project reference to `AaronOS.Modules.Finance` and one line **appended**
(not prepended) to the module array in `App.xaml.cs`:

```csharp
IAppModule[] modules = [new BodyMeasurementsModule(), new FinanceModule()];
```

Appending, rather than prepending, matters here: `MainWindow` navigates to `NavView.MenuItems[0]`
on load, so this keeps Body Measurements as the app's default landing page and just adds Finance
as a second nav entry, preserving current behavior.

### csproj

```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<LangVersion>13.0</LangVersion>
<Nullable>enable</Nullable>
```

Package references: `LiveChartsCore.SkiaSharpView.WPF` (pin to the same `2.0.5` BodyMeasurements
uses), `WPF-UI` (pin to the same `4.3.0`), `Microsoft.Web.WebView2` (net-new to the solution —
needed for the Plaid Link page; the WebView2 Runtime itself ships with Windows 11 already, so no
extra machine setup), `System.Security.Cryptography.ProtectedData` (DPAPI encrypt/decrypt).

## Data model

Three entities under `AaronOS.Modules.Finance/Data/`, each with an `IEntityTypeConfiguration<T>`,
auto-discovered by `AaronOsDbContext.OnModelCreating` (which iterates every registered module's
assembly) — no edits to `AaronOsDbContext` itself. All three live in the one shared
`aaronos.db`. Names are `Finance`-prefixed (except `PlaidItem`, which is unambiguous on its own)
so they can't collide with any future module's tables.

- **`PlaidItem`** — one row per linked bank connection.
  `Id` (int PK), `ItemId` (Plaid's id, unique), `InstitutionId`, `InstitutionName`,
  `AccessTokenEncrypted` (`byte[]`, DPAPI-protected, current-user scope), `Cursor` (`string?`,
  null until the first `transactions/sync` call), `CreatedAt` (`DateTimeOffset`).
- **`FinanceAccount`** — one row per account within an item (checking, savings, credit card).
  `Id` (int PK), `PlaidAccountId` (unique), `PlaidItemId` (FK → `PlaidItem.Id`), `Name`, `Mask`,
  `Type`, `Subtype`, `CurrentBalance` (`decimal?`), `AvailableBalance` (`decimal?`),
  `IsoCurrencyCode`.
- **`FinanceTransaction`** — `Id` (int PK), `PlaidTransactionId` (unique), `FinanceAccountId` (FK
  → `FinanceAccount.Id`), `Date` (`DateOnly`), `Name` (merchant/description), `Amount` (`decimal`;
  Plaid convention: positive = money out), `Pending` (`bool`), `CategoryPrimary` (`string?`),
  `CategoryDetailed` (`string?`), `IsoCurrencyCode`.

No EF migrations, consistent with the rest of the app (`EnsureCreatedAsync()` only) — this is a
solution-wide convention, not something to break for one module even though `dotnet-ef` would now
technically work against a UI-free `AaronOS.Core`.

## Plaid Link flow

1. `LinkAccountViewModel` calls `PlaidApiClient.CreateLinkTokenAsync()` — a `POST
   /link/token/create` with `client_id`/`secret` (see Credential storage), `client_name`,
   `products: ["transactions"]`, `country_codes: ["US"]`, `language: "en"` — returns a
   `link_token`.
2. `LinkAccountPage` hosts a `Microsoft.Web.WebView2.Wpf.WebView2` control navigated to a small
   local HTML file that loads Plaid's `link-initialize.js` and calls
   `Plaid.create({ token, onSuccess })`.
3. On success, the page's JS posts `{ publicToken, institutionId, institutionName }` back to C#
   via `window.chrome.webview.postMessage`.
4. `LinkAccountViewModel` calls `PlaidApiClient.ExchangePublicTokenAsync(publicToken)` (`POST
   /item/public_token/exchange`) to get `access_token` + `item_id`, then `PlaidApiClient
   .GetAccountsAsync(accessToken)` (`POST /accounts/get`) once to seed `FinanceAccount` rows, then
   saves a new `PlaidItem` row with the encrypted access token.

`PlaidApiClient` owns a single static `HttpClient` (no `IHttpClientFactory` — this app doesn't
register `Microsoft.Extensions.Http` today, and one long-lived client is enough for a
single-process desktop app).

## Credential storage

Two tiers, both DPAPI-protected (`ProtectedData.Protect`/`Unprotect`, current-user scope):

- **App-level** (`client_id`/`secret`, entered once): a small "Connect Plaid" prompt shown by the
  Finance module the first time it's opened with none stored, written to an encrypted file under
  `%LocalAppData%\AaronOS\` via `PlaidCredentialStore`.
- **Per-item** (`access_token`): stored directly in `PlaidItem.AccessTokenEncrypted`, in the same
  SQLite row it belongs to, rather than a separate secret store.

Neither secret is ever written to the SQLite db in plaintext or logged.

## Sync

A manual "Sync Now" command on `FinanceDashboardViewModel`, run per `PlaidItem`:

1. Call `POST /transactions/sync` with the item's access token and stored `Cursor` (null on first
   run).
2. Upsert `added`/`modified` transactions by `PlaidTransactionId`; delete rows matching `removed`
   transaction ids.
3. Save the new `next_cursor` onto the `PlaidItem` row.
4. Refresh `FinanceAccount` balances via `POST /accounts/get`.

No background timer or scheduled sync in v1 — the user triggers it manually.

## Category aggregation

Spend-by-category chart (`LiveChartsCore.SkiaSharpView.WPF`, same package/version
`BodyMeasurements` uses) sums `Amount` grouped by `CategoryPrimary` for the current calendar
month, **excluding** Plaid's `TRANSFER_IN`/`TRANSFER_OUT` categories so internal money movement
between the user's own linked accounts doesn't get double-counted as spend. This exclusion is a
judgment call and is called out with a comment at the point it's applied in code.

## UI / navigation

`FinanceShellPage` (the module's `HomePageType`) — a row of buttons plus an internal `Frame`,
exactly like `BodyMeasurementsShellPage` — navigates via `Frame.Navigate(new SomePage())` between:

- **`FinanceDashboardPage`** — total balances, account list, "Add Account" (→ `LinkAccountPage`),
  "Sync Now", recent transactions (last 30 days), spend-by-category chart.
- **`FinanceTransactionsPage`** — full transaction list, filterable by account.
- **`LinkAccountPage`** — hosts the Plaid Link `WebView2` flow described above.

ViewModels derive from `AaronOS.Core.ViewModelBase` (carries `IsBusy`). Each Page resolves its
ViewModel in its constructor via `AppServices.Provider.GetRequiredService<T>()`, sets
`DataContext` explicitly, and kicks off loading from the `Loaded` event — matching the pattern in
`docs/MODULE_GUIDELINES.md` exactly (no navigation-lifecycle overrides).

## Error handling

- Plaid API failures during sync (network errors, non-`ITEM_LOGIN_REQUIRED` API errors): caught,
  surfaced as an inline error message on the ViewModel, last successful data stays on screen — no
  crash, no silent failure.
- `ITEM_LOGIN_REQUIRED`: out of scope for v1 (see Scope) — shows a static "reconnect needed"
  banner on the affected account with no working reconnect action yet.

## Testing

This repo has no test project yet, and this design doesn't add a general test culture — just one
small, targeted project for the money-path logic that's genuinely worth a guard:

`AaronOS.Modules.Finance.Tests` (xunit, plain `net8.0`, no WPF/`UseWPF`) covering, as pure
functions/services with no DB or WebView2 dependency:

1. DPAPI encrypt/decrypt round-trip.
2. `transactions/sync` upsert logic (added/modified/removed) against an in-memory list.
3. The category-aggregation sign/exclusion rule.

This is possible without any of the design-time-tooling workarounds `MODULE_GUIDELINES.md`
describes, because `AaronOS.Core` (and this module) have zero UI-framework dependency now that
the solution is WPF.
