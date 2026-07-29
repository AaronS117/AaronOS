"""Operational health of the live paper run. Read-only; safe to run at any time.

    python scripts/trading-health.py

This exists because a process being alive is not evidence that its work is happening. The run once sat
up for six hours with the market open and fired nothing, and the only symptom was a decision log whose
newest entry was from the previous day.

It answers operational questions, not "is it making money". A one-week shakedown is about whether the
machinery runs unattended, and profitability was already settled elsewhere: the agent buys the index and
holds it.
"""
import datetime as dt
import json
import os
import sqlite3
import sys
import urllib.request

DB = os.path.join(os.environ["LOCALAPPDATA"], "AaronOS", "aaronos.db")
CREDS = os.path.join(os.environ["LOCALAPPDATA"], "AaronOS", "trading-credentials.dat")


def alpaca(path):
    """Live broker state. Keys are DPAPI-encrypted, so this shells out to PowerShell to read them."""
    import subprocess
    ps = (
        "$b=[IO.File]::ReadAllBytes('" + CREDS.replace("\\", "\\\\") + "');"
        "Add-Type -AssemblyName System.Security;"
        "$j=[Text.Encoding]::UTF8.GetString("
        "[Security.Cryptography.ProtectedData]::Unprotect($b,$null,'CurrentUser'));"
        "$c=$j|ConvertFrom-Json;"
        "$h=@{'APCA-API-KEY-ID'=$c.AlpacaKeyId;'APCA-API-SECRET-KEY'=$c.AlpacaSecret};"
        "(Invoke-RestMethod -Uri 'https://paper-api.alpaca.markets" + path + "' -Headers $h)|ConvertTo-Json -Depth 4"
    )
    out = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                         capture_output=True, text=True, timeout=90)
    return json.loads(out.stdout) if out.stdout.strip() else None


def main():
    if not os.path.exists(DB):
        print("no database yet")
        return 1

    c = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
    cfg = c.execute(
        "SELECT IsEnabled,CycleIntervalMinutes,StartedOn,Watchlist,MaxPositionPercent,"
        "MaxInvestedPercent,MaxTradesPerDay,BroadIndexSymbols FROM TradingConfig LIMIT 1").fetchone()

    now = dt.datetime.now(dt.timezone.utc)
    print(f"=== trading health at {now:%Y-%m-%d %H:%M} UTC ===\n")
    print(f"  enabled        {bool(cfg[0])}   every {cfg[1]} min   started {cfg[2]}")
    print(f"  watchlist      {cfg[3]}")
    print(f"  caps           {cfg[4]}% per company / {cfg[5]}% invested / {cfg[6]} orders a day")
    print(f"  index exempt   {cfg[7] or '(NONE — per-company cap applies to the index)'}")

    rows = c.execute(
        "SELECT RanAtUtc,ActionSummary,Error FROM AgentDecision ORDER BY RanAtUtc DESC LIMIT 400"
    ).fetchall()

    print(f"\n  decisions      {len(rows)}")
    if rows:
        last = dt.datetime.fromisoformat(rows[0][0][:19]).replace(tzinfo=dt.timezone.utc)
        age = (now - last).total_seconds() / 60
        stale = age > cfg[1] * 3
        print(f"  last cycle     {rows[0][0][:19]} UTC  ({age:.0f} min ago)"
              f"{'   <-- STALE, the schedule may have stopped' if stale else ''}")
        errored = sum(1 for r in rows if r[2])
        print(f"  errored        {errored} of the last {len(rows)}"
              f"{'   <-- investigate' if errored else ''}")
        for r in rows[:3]:
            print(f"    {r[0][:19]}  {r[1]}{'  ERR: ' + r[2][:60] if r[2] else ''}")

    orders = c.execute(
        "SELECT SubmittedAtUtc,Side,Quantity,Symbol,FilledPrice,Status FROM TradeOrder "
        "ORDER BY SubmittedAtUtc DESC LIMIT 10").fetchall()
    print(f"\n  orders         {c.execute('SELECT COUNT(*) FROM TradeOrder').fetchone()[0]} total")
    for o in orders[:5]:
        print(f"    {o[0][:19]}  {o[1]} {o[2]} {o[3]} @ {o[4]}  {o[5]}")

    snaps = c.execute("SELECT Date,Equity,Cash FROM PortfolioSnapshot ORDER BY Date").fetchall()
    print(f"\n  daily snapshots {len(snaps)}")
    for s in snaps[-5:]:
        print(f"    {s[0]}  equity {float(s[1]):,.2f}  cash {float(s[2]):,.2f}")

    acct = alpaca("/v2/account")
    if acct:
        cash = float(acct["cash"])
        equity = float(acct["equity"])
        print(f"\n  broker         equity {equity:,.2f}   cash {cash:,.2f}")
        # The check that matters most. Negative cash means the no-borrowing guardrail was bypassed,
        # which happened once and is the reason this line exists.
        print(f"  BORROWED?      {'YES — STOP AND INVESTIGATE' if cash < -1 else 'no'}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
