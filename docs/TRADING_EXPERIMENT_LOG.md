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
| 2026-07-28 | tune | baseline | nothing — the live configuration as first armed | pending | | | | | |

## Validation looks spent

| # | Date | Label | Reason | Outcome |
|---|---|---|---|---|
| 1 | — | — | unspent | |
| 2 | — | — | unspent | |
| 3 | — | — | unspent | |
