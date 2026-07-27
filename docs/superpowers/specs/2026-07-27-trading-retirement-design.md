# Trading, Wagering and Retirement — design

Date: 2026-07-27

## Why this exists

The ask was one module covering stock trading, prediction markets (Kalshi), daily-fantasy props
(PrizePicks), and retirement and savings planning, with whatever safety parameters that combination
needs. Those turn out to have very different difficulty and very different risk, so this document
splits them into phases and records the reasoning, including the parts that were deliberately not
built.

Two constraints came from the user directly. Positions and bets are tracked, never placed — but the
data model should not have to be rewritten if Kalshi order placement is added later. And retirement
and savings come first.

## Phase 1 — Retirement and savings (built)

Lives inside `AaronOS.Modules.Finance` rather than in a new module. Retirement balances, savings
buckets and spending are the same accounts and the same Plaid link, and `docs/MODULE_GUIDELINES.md`
forbids one module reaching into another's tables. A separate module would have meant inventing a
cross-module abstraction to work around a boundary that should not exist here.

### Data

Three new tables, picked up automatically by `SchemaBootstrapper` without touching existing ones.

- `RetirementPlan` — a single row of planning assumptions: salary, current age, target retirement
  age, expected return, inflation, withdrawal rate, HSA coverage type. Age lives here rather than in
  Core's `UserProfile` because the Medical module is being developed against `UserProfile`
  concurrently, and a target retirement age is a planning input rather than a fact about the person.
- `RetirementAccount` — name, tax treatment, planned annual contribution, employer match expressed
  as a percentage and a cap, and either a linked `FinanceAccount` or a manually entered balance.
- `SavingsGoal` — name, kind, target amount or months of expenses, monthly contribution, and the
  same linked-or-manual balance.

Enums are stored as text so the columns stay readable and reordering the enum cannot silently
reinterpret existing rows.

There is no contributions-history table. Transfers into these accounts already appear in
`FinanceTransaction`; a second copy would be duplicated data. If deriving history from transactions
proves unreliable, that is when to add it.

### Logic

- `RetirementProjector` — compound growth of a balance plus recurring contributions, reported in
  today's dollars as well as nominal. Contributions are added at the end of each year, which
  understates the result slightly and keeps every figure checkable by hand. Three labelled return
  scenarios rather than a Monte Carlo simulation: a distribution built on an invented volatility
  looks more authoritative without being more correct.
- `ContributionLimits` — IRS caps for 2026, taken from irs.gov and hardcoded because no free API
  publishes them. The year is exposed so the UI can say which year it is quoting. Caps are checked
  per *group*, not per account: a traditional and a Roth 401(k) share one elective-deferral cap, and
  a traditional and a Roth IRA share one IRA cap, so checking accounts individually would report
  headroom that does not exist.
- `MonthlySpendCalculator` — average monthly outflow over the most recent complete months, which is
  what sizes an emergency fund. It excludes the partial current month, transfers between the user's
  own accounts, and months with no transactions at all. Each exclusion makes the figure larger and
  the target more demanding, which is the direction an error here should always go in.
- `SpendFilter` — the single definition of "money that actually left your hands", shared with the
  dashboard's category breakdown so the emergency fund is never sized from a different number than
  the one shown on screen.

### UI

One page, `RetirementPage`, added as a third button on the Finance shell nav. Hero balance with
annual contributions and savings rate; the assumptions row; the projection chart with its three
scenario lines and the figures below it doubling as the legend; contribution caps; emergency-fund
progress in months of expenses covered; then editable lists of accounts and goals.

Two honesty rules are built into the presentation. Every projection is labelled as an estimate with
its assumed rate visible, and the chart plots today's dollars rather than nominal, because a large
nominal figure thirty years out is the most misleading thing a retirement projection can show.

### Verified

49 new unit tests. The page was also rendered offscreen at two widths with WPF's binding trace
enabled, which found three real defects that compile cleanly and fail silently at runtime: nullable
numeric bindings that WPF cannot convert (fixed by `NullableNumberConverter`), assumption fields
whose spin buttons clipped their own values so 65 rendered as "6!", and field rows that overflowed
the card below roughly 900px (fixed with wrapping panels).

### Not built

Plaid's `investments` product, so 401(k) balances are entered by hand. Adding it needs Link re-run
in update mode, and it is unknown whether the relevant providers are reachable at all. Also out of
scope: tax-aware withdrawal ordering, Social Security estimates, Roth conversion modelling, and a
target-date picker on savings goals (the column exists; nothing computes from it yet).

## Phase 2 — Wagering (not built)

Its own module, because retirement planning and sports betting have nothing to do with each other
and mixing them normalises exactly the failure mode worth designing against.

Safety parameters come first, not last:

- The wagering bankroll is its own pool and can never be funded from an account tagged retirement.
- A per-bet cap as a fraction of bankroll (fractional Kelly, 1–2%).
- Daily, weekly and monthly loss limits that lock the entry form when reached.
- Escalation detection: flag when average stake rises following losses, the measurable signature of
  chasing.
- Lifetime net always on the dashboard, never just the current streak. This one number does more
  protective work than every other rule combined.
- 1-800-GAMBLER in the module footer.

Then the mechanics: a bet log with settlement, and odds maths (American/decimal/implied conversion,
vig removal to a fair line, expected value, closing-line value). All pure functions, all testable,
no external data required. Comparing your own number against the market's no-vig consensus is the
one real analytical edge available to a retail bettor.

The trading data model separates a position held from an order placed, so Kalshi order placement can
be added later without a rewrite.

## Phase 3 — Live data (not built)

Medium difficulty and each piece needs an account or a key: market quotes from a rate-limited free
tier with a local cache, Kalshi market data over its public REST API. PrizePicks has no public API
and blocks scraping, so its projections stay manual.

WhatsApp stock tips have no read API for a personal account, and automating WhatsApp Web violates
its terms. The workable route is WhatsApp's own **Export Chat** feature: an importer parses the
exported `.txt`, extracts ticker mentions with who said them and when, and — once quotes exist —
scores what the price actually did afterwards. Same shape as the existing Withings importer.

## Explicitly recommended against

A model that predicts game outcomes well enough to beat a sportsbook. Clearing a standard -110 line
requires winning above 52.4%, and the books already price injury news, weather and lineup changes
within seconds. A model trained on freely available data will not clear that bar, and it is the most
expensive part of this project with the least likely payoff.

Where an LLM does earn its keep is narrow: parsing a bet-slip screenshot into structured rows,
explaining what a Kalshi market's settlement rules actually mean, summarising earnings news for a
ticker. Each is a single HTTP call with a key stored the same DPAPI way as the Plaid secret.
