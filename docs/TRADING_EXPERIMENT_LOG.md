# Trading experiment log

Every configuration replayed, and every look at the held-out window. This file exists to make the
search visible: the number of things tried is the denominator on any apparent improvement, and a
backtest log that records only the winner is how a strategy comes to look better than it is.

Append, never rewrite. A result that turned out to be noise is still part of the record.

## Rules agreed before any data existed

- **Tune window:** 2025-08-01 → 2026-01-31. Iterate freely here.
- **Validate window:** 2026-02-01 → 2026-07-24. **Three looks total, ever.** Each one is logged below
  with the date and what it was spent on.
- If tune and validate disagree, **validate wins** and iteration stops. That is the whole point of
  holding it back.
- Tunable: prompt and strategy notes, position and exposure caps, orders per day, decision cadence.
- The live paper run is never changed on tune-window evidence alone.

## Why a replay is defensible here, and when it stops being

Measured on 2026-07-28: `qwen3:14b` scored **7/12** on a balanced forced-choice test of SPY monthly
direction (chance is 6/12), including 4/6 on months inside its own training window. It has no usable
recall of what the market did, so the lookahead mechanism that invalidates most published LLM
backtests — the model retrieving the outcome — has nothing to retrieve at this granularity. The agent's
brief also carries no date, only balances, positions and current quotes.

Two limits on that. Twelve samples cannot distinguish 7/12 from 6/12, so a small amount of knowledge
would be undetectable. And it would **not** transfer to a frontier model: swapping to Qwen3-235B on a
hosted tier very likely reintroduces recall, at which point every backtest number below becomes
meaningless and this file should be started again from scratch.

## Replay fidelity

- Fills at the **next session's open**, never the close the decision was made from.
- Spread and slippage charged on every fill, defaulting to 2 and 3 basis points, always against the
  trade. Verified by a test that a round trip in a flat market loses money.
- Daily decisions, against 30-minute cycles live. A known difference in cadence, not a bug.
- IEX prints rather than the consolidated tape, because that is what the free feed carries.

## Runs

| Date | Window | Label | What changed | Strategy | SPY | Alpha | Drawdown | Fills | Closed |
|---|---|---|---|---|---|---|---|---|---|
| 2026-07-28 | tune | baseline | nothing — the live configuration as first armed | +0.00% | +11.27% | **−11.27** | 0.00% | 0 | 0 |
| 2026-07-28 | tune | index-default | brief rewritten; SPY added to watchlist; index exempt from per-position cap | pending | | | | | |

### baseline — a defect, not a result

126 sessions, 126 holds, zero orders, zero errors, coherent reasoning every time. Equity never moved
while the index rose 11.27%.

The machinery was fine; the instruction was unsatisfiable. The brief said to judge every trade against
SPY and to hold when no reason was evident, and the watchlist deliberately excluded SPY. The only
action that could meet the stated bar had been forbidden, so permanent inaction was the *correct*
response to it. The model said as much every day: "cash preservation aligns with strategy".

Two things worth keeping from this:

- **Cash is a position and a model will not infer that.** Sitting in cash is a bet against the index
  that loses by the index's full gain. The brief now says so explicitly.
- **Guarding against churn is not the same as guarding against investing.** An anti-churn instruction
  with no floor produces paralysis, and in a rising market that is the worse of the two failures
  because it has no variance at all — a certain loss rather than a risky one.

Counted as a defect fix rather than parameter tuning. It cost no statistical budget because nothing
was being optimised: an unsatisfiable instruction was made satisfiable. Genuine tuning starts from
`index-default`.

A second-order flaw surfaced while fixing the first. The 10% per-position cap applied to SPY too, so
"hold the index" could only ever place a tenth of the account and would have reproduced the same
cash-drag failure in milder form. That cap exists to limit exposure to a single company, and a
500-company fund is not one, so broad-index symbols are now exempt from it while remaining bound by
the 80% total exposure cap and by available cash.

## Validation looks spent

| # | Date | Label | Reason | Outcome |
|---|---|---|---|---|
| 1 | — | — | unspent | |
| 2 | — | — | unspent | |
| 3 | — | — | unspent | |
