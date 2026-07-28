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

## Phase 2 — Paper trading with an autonomous agent (built)

`AaronOS.Modules.Trading`, against Alpaca's paper environment. Paper and live differ only in the
trading host, so proving a strategy out and then deciding about real money is a configuration change
rather than a rewrite. Alpaca's free data tier serves IEX only, a few percent of total volume, so
paper fills run slightly optimistic — fine for measuring a strategy, not exact.

### Guardrails, enforced by the application

Every order the model emits passes through `TradingGuardrails` before it reaches the broker: the
symbol must be on the watchlist, no single position may exceed a percentage of equity, total
exposure is capped so cash is always held back, there is a daily order limit, and both borrowing and
short selling are refused outright. A refusal is returned to the model as a tool result so it can
react within the cycle, and written to the decision log so it is visible afterwards.

The distinction that matters: these are constraints, not instructions. A model asked to respect a
position limit mostly will, and the exceptions are precisely the cases a risk control exists for.
The rules are pure, synchronous functions with no clock, network or database, which is what makes
them directly testable — the only way to know a safety layer works before the day it has to.

### Measurement

Placing paper trades is easy; knowing whether there is an edge is the hard part, and paper trading
systematically overstates performance. Four structural answers rather than optional ones:

- Return is always reported beside SPY over the identical window. Twelve percent against an index
  that returned fifteen is a loss, and the dashboard says so in those words.
- `StartedOn` is stamped by the first cycle and never rewritten, so a bad run cannot be restarted and
  counted from the recovery.
- The win rate is withheld entirely below a usable sample. A run of eight winners reads like skill.
- Round trips are counted only once closed. Marking open positions to market lets winners count
  while losers stay open, which is the easiest way to flatter a record.

### Which model, and running it for nothing

The provider is pluggable through `IAgentProvider`, with two implementations: Anthropic's Messages
API, and one adapter for anything speaking the OpenAI chat-completions format. The second is one
adapter rather than one integration per vendor because that format is the de facto standard — Ollama
and LM Studio serve it locally at zero cost, and Groq, Gemini and OpenRouter all expose it on free
tiers whose daily caps sit far above the roughly thirteen cycles a trading day needs. Switching is a
base URL and a dropdown.

Two findings from researching this are worth recording, because both cut against the intuition.

First, a *dedicated* trading model does not help. There is a real category of finance-specific
time-series foundation models — Chronos, TimesFM, Moirai, Kronos — free on Hugging Face and built for
exactly this shape of problem. The evidence is discouraging: *Pretrained Time-Series Foundation
Models for Financial Return Forecasting* (arXiv 2606.27100) finds they are "useful practical priors
that reduce model-development costs in low-data financial forecasting, but are not universal engines
for statistically reliable alpha generation", with gains over a random walk "small and sparse" —
statistically significant in two of ten model-and-stock pairings, and profitability after costs not
examined at all. A purpose-built model would be differently mediocre, not better.

Second, the cost of a frontier model here is small. Logged cycles run about 1,900 tokens in and 250
out; at thirty-minute intervals across a session that is roughly 30,000 tokens a day in total. The
expense reputation comes from continuous coding workloads, not from thirteen small calls.

Because a local model may emit malformed tool arguments — a well-documented weakness — argument
reading goes through `ToolArguments`, which answers "absent" rather than throwing, and a bad call is
refused and explained back to the model instead of ending the cycle. The tool surface is held at two
for the same reason: local models degrade quickly past about three. The guardrail layer means a weak
model is a nuisance rather than a hazard, which is what makes experimenting with one reasonable.

### On the model doing the trading

Built at the account owner's explicit direction after the recommendation below was given and
understood. The honest framing is that this is a research instrument rather than an edge: it produces
a logged, benchmarked answer to "does an LLM trader beat holding the index", which is worth owning,
and it is not a reason to fund the strategy. The system prompt says plainly that doing nothing is
usually correct, because an agent asked every half hour what to trade will find something to trade,
and churn is the most reliable way to lose to the index.

Autonomy here means autonomous while AaronOS is open. It is a desktop application; closing the window
stops the trading. Running overnight needs a server, which is separate work.

### What the published evidence actually shows

Asked whether LLM agents trade profitably, the honest answer is that it is unproven, and the reason
most published results cannot settle it is worth recording.

The disqualifying problem is lookahead contamination. A model trained on the internet through some
cutoff has already read what happened to every ticker before that date, so a backtest over that
period measures recall rather than forecasting. [*Detecting Lookahead Bias in LLM
Forecasts*](https://arxiv.org/abs/2512.23847) makes this measurable: their Lookahead Propensity
metric stays materially positive throughout the training window and "collapses essentially to zero
right after the training-data cutoff." When a model can recall the outcome, forecasting ability is
formally non-identified — there is no way to separate skill from memory. A survey of the field found
that of 19 primary studies, 2 disclosed a time-consistent data split, 1 specified transaction costs,
and none reached the top reproducibility tier.

The cleanest evidence available is [StockBench](https://arxiv.org/abs/2510.02209), built
contamination-free by testing only after the models' knowledge cutoffs, over 82 trading days in 2025.
Buy-and-hold returned 0.4%; the best agent returned 2.5%; the worst returned −2.8%. Eleven of
fourteen models beat the passive baseline on raw cumulative return, and the authors' own conclusion is
nonetheless that "most LLM agents fail to outperform this simple baseline in terms of both cumulative
return and risk-adjusted return" — the risk-adjusted picture is worse than the raw one. A live
two-month benchmark, [Agent Market Arena](https://arxiv.org/abs/2510.11695), reports agents often
beating buy-and-hold across four assets, and finds agent architecture matters more than which model
sits underneath.

Read together: a small, possibly-noise edge over a nearly flat baseline, across a few months and a
handful of tickers, mostly failing on risk-adjusted terms. That is not proof of profitability. It is
grounds for running the experiment rather than grounds for expecting it to work.

One finding does bear directly on model choice: in StockBench the top performers were open-weight
models — Qwen3, GLM-4.5, Kimi-K2 — not the frontier proprietary ones. Combined with Agent Market
Arena's conclusion that architecture dominates the backbone, running this on a free local model is
not a compromise on the evidence available.

For real money over a long window, the closest analogue is the AI-powered equity ETF AIEQ: live since
2017, roughly in line with the index before its 0.75% fee, behind after it.

### The success criterion, fixed in advance

Because a bar that can move is not a bar, this is what "profitable" has to mean, decided before the
data exists:

- At least six calendar months running, **and** at least 30 closed round trips. Fewer trades than
  that is inconclusive, not a pass.
- Total return ahead of SPY over the identical window by at least two percentage points.
- Maximum drawdown no worse than SPY's over that window.
- Measured from the `StartedOn` stamped by the first cycle, which the code never rewrites.

Failing means the answer is no. Passing means it is worth continuing to watch, not that it is worth
funding — six months is one market regime, and 30 trades is the floor of meaningfulness rather than a
comfortable sample.

### Verified

70 tests. Beyond the guardrail and measurement units, a full cycle is driven end to end against a
scripted model and a scripted broker: an allowed order reaches the broker and is stored with its
reasoning, a refused order never reaches it while still appearing in the log, malformed tool arguments
are refused without ending the cycle, the daily cap holds across several orders inside one cycle, the
model is not called at all while the market is closed, and the start date is stamped once and left
alone. That proves the machinery, and nothing about the quality of the decisions.

Both pages were rendered offscreen with WPF's binding trace enabled, and the app was launched to
confirm the new tables and columns are created against the live database without disturbing it.

## Phase 3 — Wagering (not built)

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

## Phase 4 — Remaining live data (partly built)

Equity quotes now come from Alpaca as part of Phase 2. Still outstanding: Kalshi market data over its
public REST API, whose demo environment at `demo-api.kalshi.co` mirrors production with separate
credentials and RSA-key auth. PrizePicks has no public API and blocks scraping, so its projections
stay manual.

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
