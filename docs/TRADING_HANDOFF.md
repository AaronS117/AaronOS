# Trading module — handoff

Written 2026-07-30 at the end of the build. Read this before touching the Trading module, before
proposing anything that involves beating a market, and before building the budgeting module.

## The conclusion, so nobody repeats the work

An LLM agent was built to trade, tested exhaustively, and **does not produce an edge**. This is settled
by evidence, not opinion, and the evidence is in `TRADING_EXPERIMENT_LOG.md`.

Three independent tests, three consistent answers:

| Test | Result |
|---|---|
| Agent trading equities, 11 configurations, one 6-month window | Best config *became* buy-and-hold and matched the index to −0.06 points. Forced to pick stocks it returned −0.91% while the index made +11.27%. |
| Agent forecasting real events, 4,254 resolved Kalshi markets | Brier 0.2454 against the market's 0.0435, and **worse than a constant guess** (0.2194). When it disagreed with the price by 10+ points it was closer to the truth 11.8% of the time — 50% is what noise looks like. |
| Eight mechanical timing rules for re-entry after a stop | **None beat holding.** The rules that waited for a confirmed recovery lost 70–92 points and three ended with a *deeper* drawdown than no stop at all. |

Enabling reasoning on the forecasting test improved Brier by 0.0113 at 4.4× the compute — about 5% of
the gap to the market. It did not change the conclusion.

**Do not propose:** a better prompt, a bigger model, a different indicator, sentiment analysis, analyst
price targets, or copying disclosed insider/congressional trades. The last was tested against the two
live ETFs that do it: NANC beat SPY only by being a tech fund and lost 26 points to QQQ, its actual
benchmark; KRUZ, same method, trailed SPY by 55 points. A 72-point spread between two funds running one
strategy is noise, not signal.

**A frontier model might score better** on forecasting — published work puts good ones within 0.017
Brier of superforecasters against this model's 0.21 gap. But it reintroduces training-data contamination
on any retrospective test and costs money per question. The harness supports it via a base-URL change if
anyone wants to try; the calibration scoring is already built.

## What is running right now

A live paper-trading run on Alpaca, started 2026-07-29, reviewed 2026-08-05.

```
provider   openai-compatible -> http://localhost:11434/v1   (Ollama, qwen3:14b, free)
cadence    every 15 minutes while the market is open
watchlist  SPY,QQQ,AAPL,MSFT,NVDA,AMZN,GOOGL,META,AVGO,TSLA
caps       10% per company · 30% stock sleeve · 100% invested
stop       7% trailing, then a 20-day cooldown before repurchase
```

It is an **operational** test — does the machinery run unattended — not a profitability test. Expect it
to buy SPY and hold. Check it with:

```
python scripts/trading-health.py
```

The line that matters is `BORROWED?`. It said YES once, when a bug let a $100k account buy $188k on
margin. If it ever says YES again, stop the app and investigate before anything else.

To stop the run entirely: set `IsEnabled = 0` in the `TradingConfig` row. That single switch governs
everything; the scheduler arms from it at app startup.

To stop the app launching at login:
`Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name AaronOS`

## What is worth keeping

The **harness**, not the strategy. It graded eleven configurations and 4,500 forecasts in a day and
caught six defects that would have cost real money.

- `Backtest/` — replays the real agent through historical sessions. Fills at the *next* session's open
  with spread and slippage charged; same-bar fills are lookahead in disguise.
- `Backtest/BaselineStrategies.cs` — mechanical rules the agent is measured against. An agent result
  without arithmetic beside it is a weak finding.
- `Trading/TradingGuardrails.cs` — pure, synchronous, heavily tested. Every limit lives here rather than
  in a prompt.
- `Forecasting/CalibrationScoring.cs` — Brier and calibration against a market price, refusing to call
  anything an edge unless it beats the price and has 100+ samples.
- `scripts/trading-health.py` — operational health, because a process being alive is not evidence its
  work is happening.

## The failure mode this module kept producing

**Six times, a broken thing produced a plausible number instead of an error.** Each was found by
accident. This is the single most transferable lesson in the repo and applies to any module that
reports a metric:

1. Buy-and-hold reported +0.00% with zero fills — blocked by a cash margin, read as caution.
2. Trend following reported +0.00% — a 252-day rule with 30 days of history, read as a finding about
   trend following.
3. Three sells produced zero closed trades — instant fills skipped reconciliation, so the fill price
   stayed null and the live 30-trade gate could never have opened.
4. A run whose every cycle errored reported "+0.00%, alpha −11.27" — the model server was down.
5. `BroadIndexSymbols`, declared `"SPY,QQQ,VTI,VOO,IVV"`, was backfilled into the live database as `""`,
   silently disabling the index exemption and leaving the agent 90% in cash.
6. A stop-loss with no cooldown sold and rebought 15 minutes later, paying the spread twice and
   protecting nothing.

Where the output of a failure has the same shape as the output of a decision, the failure will
eventually be read as a finding. The defence is not care — it is making the two shapes different.
`SchemaBootstrapper` now backfills from the entity's real C# default, `BacktestResult.IsUntrustworthy`
refuses to print a return for a run that mostly errored, and `BaselineRunner` reports its first refusal.

## For the budgeting module

The trading work established the ranking that matters, and it is not about returns.

At $300/month for 30 years at 7%:

| Decision | Worth |
|---|---|
| Roth IRA instead of a taxable account | **$40,241** |
| VOO/VTI (0.03%) instead of an active fund (0.50%) | **$31,979** |
| Beating the market by a full point a year | $80,562 — and unachievable on this evidence |

And saving $300 more a month for 20 years is worth **$156,278**, against **$29,392** for beating the
market by two points a year. **The savings rate is 5.3× the lever that returns are**, which is why the
budgeting module matters more than everything in `AaronOS.Modules.Trading`.

What already exists to build on:

- `Modules.Finance/Sync/MonthlySpendCalculator.cs` — true average monthly outflow, excluding transfers
  between the owner's own accounts and the partial current month. Both exclusions push the figure up,
  which is the only safe direction for an error in a savings target.
- `Modules.Finance/Sync/SpendFilter.cs` — the single shared definition of "money that left your hands".
  Use it rather than writing a second one; two definitions eventually disagree.
- `Modules.Finance/Retirement/` — contribution caps checked per shared IRS group (a traditional and a
  Roth 401k share one cap), and projections reported in today's dollars.
- 437 real Plaid transactions in the live database. The owner's spending is knowable from data rather
  than from asking him.

Owner context worth knowing: he is new to investing, has been trading on Robinhood at roughly $60 scale,
and his employer does **not** match his 401k — so the ranking is Roth IRA first, then a 401k only if its
fund menu is cheap. At his current balance no strategy matters and only deposits do.

## Secrets

DPAPI-encrypted under `%LocalAppData%\AaronOS\`, never in the repo, which is **public**:

- `plaid-credentials.dat`, `trading-credentials.dat`, `alpaca-recovery.dat`

Scan tracked files for known secret substrings before every push. The Alpaca paper key was pasted into a
chat transcript and should be regenerated.

## Open, deliberately not done

- The three validation-window looks are **unspent**. There is nothing left to validate — a strategy that
  places one order does not need a held-out window.
- Kalshi trading was never built. Market data is public and needs no account; fees are
  `ceil(0.07 × P × (1−P) × 100)/100` per contract, so an edge must beat the price by ~1.75 points at even
  money. The calibration test says this model cannot.
- Tax-loss harvesting is the largest genuinely edge-bearing thing not built: roughly 0.2–1.0% a year,
  mechanical, no forecast required, taxable accounts only.
- The stalled-scheduler root cause was never established. A watchdog re-arms it after two missed
  intervals; that is mitigation, not diagnosis.
