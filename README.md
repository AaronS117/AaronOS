# AaronOS

A personal, modular WPF desktop app for Windows. AaronOS runs entirely on the user's own
machine — its data (measurements, financial balances/transactions) lives in a local SQLite
database and is never sent anywhere except to the third-party APIs each module explicitly
integrates with (e.g. Plaid for bank data), under the user's own credentials.

## What it does

AaronOS is built around a small, well-defined module contract (see
[`docs/MODULE_GUIDELINES.md`](docs/MODULE_GUIDELINES.md)) so new feature areas can be added over
time without disturbing the ones already there. Current modules:

- **Body Measurements** — logs weight and body measurements over time, computes BMI, tracks
  clothing sizes, and supports weight-loss/muscle-gain goals with progress tracking.
- **Finance** — links bank accounts via [Plaid](https://plaid.com), syncs balances and
  transactions, and shows spend broken down by category.

## Tech stack

.NET 8, WPF, [WPF-UI](https://github.com/lepoco/wpfui) for Fluent/Mica styling, EF Core + SQLite
for storage, CommunityToolkit.Mvvm for MVVM.

## Status

This is an actively-developed personal project, not a published product — expect rough edges.
