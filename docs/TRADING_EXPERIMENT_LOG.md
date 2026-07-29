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
| 2026-07-28 | tune | index-default | brief rewritten; SPY added to watchlist; index exempt from per-position cap | +1.08% | +11.27% | −10.19 | 0.49% | 7 | 0 |
| 2026-07-28 | tune | weekly-indexaware | as above, weekly decisions instead of daily | +7.16% | +11.27% | −4.11 | 4.07% | 6 | 4 |
| 2026-07-28 | tune | **daily-100cap** | exposure cap 80→100%; brief now states the index exemption | **+11.21%** | +11.27% | **−0.06** | 5.05% | 1 | 0 |
| 2026-07-28 | tune | weekly-100cap | as above, weekly decisions | +9.45% | +11.27% | −1.82 | 5.07% | 2 | 0 |

### Mechanical baselines, same window, same fills, same guardrails

| Strategy | Return | Alpha | Drawdown | Fills |
|---|---|---|---|---|
| buy-and-hold SPY | +11.21% | −0.06 | 5.05% | 1 |
| trend following (252d) | +11.21% | −0.06 | 5.05% | 1 |
| vol-targeted (15%) | +11.21% | −0.06 | 5.05% | 1 |
| equal weight, monthly | +6.54% | −4.73 | 5.81% | 10 |

This window does not discriminate between the first three: SPY trended up throughout and realised
volatility stayed under target, so trend following never exited and vol targeting never scaled down.
All three are buy-and-hold under other names here. Equal-weighting into individual megacaps was worse
on both return and drawdown, which is a genuine result for this watchlist.

### The conclusion the tune window reached

Given an honest brief and no handicap, the agent **became buy-and-hold**. One order on the first
session — 159 SPY, $99,489, essentially the whole account — then 125 sessions of holding. Alpha −0.06,
which is the spread it paid to get in. Its reasoning quoted the brief back: "cash is a bet against the
index."

That is a defensible answer and arguably the right one. It is also an answer that the judgement adds
nothing: an expensive way to execute a single index purchase.

Two consequences worth being explicit about.

**Weekly cadence was an artifact, not a finding.** At the 80% cap weekly (+7.16%) beat daily (+1.08%)
and looked like the turnover effect showing up in our own data. At the corrected cap daily (+11.21%)
beat weekly (+9.45%). The earlier gap was the daily run wasting capital fighting a phantom index cap,
exactly the alternative explanation flagged at the time. The turnover literature may still be right; this
experiment did not demonstrate it.

**The success criterion cannot be met.** It requires 30 closed round trips. An agent that places one
order and holds will produce zero. Six live months would confirm that it holds the index and nothing
else. The bar was written for a strategy that trades; the agent declined to be one, which answers the
question earlier and more cheaply than the live run would have.

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

## The recurring failure, and why it deserves its own section

Four separate times in this module a broken run produced a plausible-looking number instead of an error.
Each was found by accident, and each would have been believed if it had happened to land on a
comfortable figure.

1. **Buy-and-hold reported +0.00% with zero fills.** Sized to the invested cap, refused by the cash
   margin. Read as a cautious strategy; was a blocked one.
2. **Trend following reported +0.00%.** A 252-session lookback with thirty days of history, so it never
   had enough data to act. Read as a finding about trend following; was a finding about a fetch window.
3. **Three sells produced zero closed trades.** Orders arrived already filled, so reconciliation skipped
   them and the fill price stayed null. Read as a strategy that never closed a position; was a counter
   that could not see instant fills — and it would have kept the live run's thirty-trade gate shut
   forever.
4. **The news run reported +0.00% and alpha −11.27 with all 126 cycles errored.** The model server was
   down. Nothing in the summary distinguished it from a strategy that had chosen to hold cash.

The through-line is that a zero is ambiguous and every layer of this system was content to present one.
Three fixes now exist because of it: sizing asks only for what the guardrails will permit, the runner
names the first refusal, and a run with more than a tenth of its cycles errored refuses to report a
performance figure at all and exits non-zero. There is also a reachability probe, because a configured
endpoint is not a reachable one and the difference cost a full thirty-minute run.

Stated plainly because it generalises: in a system where the output of a failure looks like the output of
a decision, the failure will eventually be mistaken for a finding. The defence is not care, it is making
the two shapes different.

## Validation looks spent

| # | Date | Label | Reason | Outcome |
|---|---|---|---|---|
| 1 | — | — | unspent | |
| 2 | — | — | unspent | |
| 3 | — | — | unspent | |
