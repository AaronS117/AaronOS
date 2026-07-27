# Withings sleep import

Notes on the Withings API as it actually behaves, and the design decisions in
`src/AaronOS.Modules.Medical/Withings/`. Written down because most of what follows is not obvious
from the reference docs and costs real time to rediscover.

## Hardware

Withings Sleep, model `WSM02-ALL-US` (UPC 3700546703935). The same hardware sells outside the US as
the Sleep Analyzer, where extra software grades sleep apnea as breathing pauses per hour. The US
model reports a raw breathing disturbances index instead. Both expose identical sleep endpoints, so
nothing in this code depends on which variant is in the bed.

## Setup, once

1. Register an app at developer.withings.com. Free and self-serve.
2. Use `https://wbsapi.withings.net/v2/oauth2` as the callback URL. Withings only accepts HTTPS
   redirects, which a desktop app cannot serve, so the documented workaround is to register their own
   API URL and have the user copy the `code` out of the address bar after consent. This avoids
   running a local HTTP listener and the firewall prompt that comes with it.
3. Paste the client ID and secret on Medical → Sleep. They are stored DPAPI-encrypted under
   `%LocalAppData%\AaronOS\withings-credentials.dat`, never in the database.

## Three ways this is not standard OAuth2

**There is no token endpoint.** Token operations are an `action` parameter on an ordinary API path:
`POST https://wbsapi.withings.net/v2/oauth2` with `action=requesttoken` and either
`grant_type=authorization_code` or `grant_type=refresh_token`.

**Every response is HTTP 200.** The real outcome is a `status` field in the body, where `0` means
success. `response.EnsureSuccessStatusCode()` tells you almost nothing, so
`WithingsEnvelopeExtensions.Require` treats any non-zero status as a failure. A 401 arrives as
`{"status": 401, ...}` inside a perfectly successful HTTP response.

**Refresh tokens rotate.** Every refresh returns a new refresh token and invalidates the old one
immediately. Failing to persist it breaks the *next* sync rather than the current one, which presents
days later as an unrelated authentication bug. `WithingsCredentials.ApplyToken` handles this and is
covered by `WithingsCredentialsTests`.

Withings is also inconsistent about quoting numbers — the same field arrives as `12345` or `"12345"`
depending on endpoint — so `WithingsJson.Options` sets `NumberHandling = AllowReadingFromString`.

## Scopes

`user.info,user.metrics,user.activity` is sufficient for nightly sleep summaries and is what the
published sleep-mat integration guides use. `user.sleepevents` additionally covers raw in-night
events, which this app does not read.

## Nights are keyed to the morning, not the evening

The one decision most likely to be got wrong. Withings labels a sleep period by its **start** date,
so a normal 23:00–07:00 night is filed under the previous day. Sleep only means anything here
alongside the day it affects, and the mood entry for the 21st is about the night that ended on the
morning of the 21st. `WithingsSleepMapper` therefore re-keys every period to the local date of its
`enddate`.

Timestamps are unix seconds and are converted using the IANA zone the series carries, which .NET
resolves natively on Windows. An unknown zone falls back to the local machine zone: being an hour out
is recoverable, discarding the night is not.

## Multiple periods in one night

All periods ending on the same local date are combined. Durations are summed; point-in-time readings
(sleep score, heart rate average, breathing) are taken from the longest period, so a twenty-minute
nap cannot drag a night's numbers around. `SleepNight.PeriodCount` records how many were folded
together and the UI shows it whenever it exceeds one, because a fragmented night or a nap absorbed
into a total is worth seeing rather than hiding.

This rule is a judgement call made before any real data existed. If a nap regularly inflates a day,
the fix is a one-line change in `Combine`, and `PeriodCount` is what makes the problem visible.

## Storage

`SleepNight` is separate from `MoodEntry` deliberately. A mood entry is a self-report, and letting an
import overwrite hours someone typed themselves would destroy the only record of what they believed
at the time. Measured data wins on **display** — resolved in one place, `MoodStatistics.SleepFor` —
but never overwrites. Durations are stored in seconds exactly as reported, with hours computed for
display, so nothing is lost to rounding at rest.

Absent fields stay null rather than becoming zero. "This unit does not report REM" and "you got no
REM sleep" are different claims and must not render identically.

One row per wake date, enforced by a unique index rather than only by the save path, so re-running a
sync over an already-imported range updates rather than duplicates.

## Not verified against hardware

Everything above is built and unit-tested against recorded response shapes, but as of 2026-07-27 the
pad has not arrived. The OAuth round trip, the live response shape, and the real timezone values have
not been exercised against the actual service. Expect to fix something on first connection; the
mapper is where a surprise would most likely land.
