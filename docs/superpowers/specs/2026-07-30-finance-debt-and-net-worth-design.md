# Finance: Debt Tracking and Net Worth — Design

## Context

`docs/superpowers/specs/2026-07-27-trading-retirement-design.md` ("Phase 1 — Retirement and
savings") explicitly did not track debt: `RetirementAccount` and `SavingsGoal` model assets only.
The user has real federal student loan debt (Mohela-serviced, currently $34,830.80 across 8 loans
on an Income-Based Repayment plan) and wants it reflected in the Finance module's net worth, not
just tracked as a fact outside the app.

## Data model

One new entity, `DebtAccount`, in `AaronOS.Modules.Finance/Data/`, following the same shape as
`SavingsGoal`:

```csharp
public enum DebtAccountKind { StudentLoan, CreditCard, Mortgage, Other }

public class DebtAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DebtAccountKind Kind { get; set; }
    public decimal Balance { get; set; }
    public decimal? InterestRatePercent { get; set; }
    public decimal? MonthlyPayment { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }
}
```

`Balance` is the true total owed (principal plus any outstanding accrued interest — for the user's
loans today, $34,830.80, not the $32,114.00 principal-only figure), since that is the number that
belongs in a net worth calculation. A rate *range* across several individual loans, or plan-specific
detail (repayment plan name, recertification date), goes in `Notes` as free text rather than
dedicated columns — this entity is deliberately general (`Kind` covers student loans, credit cards,
a future mortgage) rather than modeling IDR-plan mechanics, matching the "general, minimal shape"
scope decision.

**Deliberately no `FinanceAccountId` link**, unlike `RetirementAccount`/`SavingsGoal`. Two reasons:
the loan servicer isn't something Plaid Link is connected to, so entry here is always manual; and if
a credit card is ever linked through Plaid, its balance is already counted correctly via
`FinanceAccount.IsLiability`/`SignedBalance` (both already exist, sign-flipping a linked credit/loan
account's balance). Letting `DebtAccount` link to the same `FinanceAccount` would double-count that
balance in net worth.

`SchemaBootstrapper` (see `src/AaronOS.Core/Data/SchemaBootstrapper.cs`) picks up the new table and
its `IEntityTypeConfiguration<DebtAccount>` automatically on next launch — no migration, no risk to
existing data, the same mechanism that added `RetirementAccount`/`SavingsGoal` to a live database.

## Net worth

A new pure calculator, `NetWorthCalculator`, alongside `RetirementProjector` and
`MonthlySpendCalculator` (same reasoning: no DB or clock dependency, independently testable):

```
NetWorth = Σ FinanceAccount.SignedBalance            (every linked account — already correctly
                                                       signed for checking/savings/credit/loan)
         + Σ RetirementAccount balance                (linked-or-manual, existing)
         + Σ SavingsGoal balance                      (linked-or-manual, existing)
         − Σ DebtAccount.Balance where !IsArchived
```

This is the "whole picture" scope: everyday checking/savings balances are included, not just
retirement accounts, per the user's choice.

## UI

Appended to the existing `RetirementPage` — no new shell nav item:

- A second headline figure, labeled distinctly as **"Net Worth"** (separate from the page's existing
  retirement-only hero balance, so the two are never confused).
- A **"Debts"** list section below the existing Goals section, using the same editable-row pattern
  (add / edit / archive) already used for `RetirementAccount` and `SavingsGoal` rows.

## Testing

`NetWorthCalculatorTests`, added to the existing `AaronOS.Modules.Finance.Tests` project — covers
the one genuinely non-trivial piece (aggregating three tables with mixed signs, excluding archived
debts). `DebtAccount` itself is a plain data class and doesn't need a dedicated test, consistent with
how `RetirementAccount`/`SavingsGoal` were treated.

## Out of scope

- Per-individual-loan tracking (the user's servicer shows 8 separate loans with individual
  subsidized/unsubsidized status and rates) — represented as one `DebtAccount` row with the
  aggregate total and the breakdown in `Notes`, not 8 rows. If per-loan tracking is ever needed,
  that is a distinct, larger feature, not an extension of this one.
- Any interaction with `RetirementProjector` or `ContributionLimits` — debt is additive to net worth
  only; it does not feed into the retirement growth projection or contribution-cap math.
- Linking a `DebtAccount` to a Plaid `FinanceAccount` — see "Data model" above for why.
