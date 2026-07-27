# Mobile data entry design

**Status: DEFERRED.** Written 2026-07-27 from a read-only investigation. Nothing has been
implemented. Picked up later if and when there's time.

**Goal:** enter data into AaronOS from an iPhone, since most of what still needs typing happens away
from the desk.

## The finding that shapes everything

The iOS client is the easy part. Three things block it, and only one of them is application code.

**There is no network surface.** `src/AaronOS.App/App.xaml.cs:55` registers
`Data Source={dbPath}` against `%LocalAppData%\AaronOS\aaronos.db`, and ViewModels query EF Core
directly. Nothing listens on a socket. Every option below begins by building an API that does not
currently exist.

**No Mac.** Native Swift requires Xcode on macOS. So does .NET MAUI when targeting iOS — the C# is
portable but building and signing an iOS binary still routes through Apple's toolchain. Without the
$99/year developer account a sideloaded build expires after 7 days and needs re-signing weekly, which
rules native out for personal use regardless.

**Entities are coupled to WPF.** `AaronOS.Modules.Medical.csproj` targets `net8.0-windows` with
`UseWPF`, and `MoodEntry`, `SleepNight` and `MoodStatistics` share that assembly with the XAML pages.
Every module is built this way. An API referencing them inherits a Windows-only WPF dependency —
fine if the API runs on the same PC, fatal if it should ever run on the Linux box.

## Recommendation: an installable PWA, not a native app

A small ASP.NET Core app serving a mobile-shaped web UI, added to the iPhone home screen. It opens
full-screen without browser chrome, a service worker provides offline capability, and web push is
available to home-screen-installed PWAs. Camera capture is a plain
`<input type="file" accept="image/*" capture>`, which also covers the photo-based ingredient entry
deferred out of the Nutrition module.

No Mac, no Xcode, no App Store review, no annual fee, and it reuses the C# already in this repo.

Native buys nothing here. The screens in question are date-keyed forms with sliders and number
fields. Paying for a Mac and a developer account to render five inputs is not a trade worth making.

## What the existing schema already gets right

Offline-first sync is normally the expensive part because of conflict resolution. Here it is close to
free. There is one user, and both `MoodEntry` and `SleepNight` are upsert-by-date behind unique
indexes, so two writes for the same day converge on last-write-wins with no ambiguity about intent.
The mood save path is already idempotent.

The phone can therefore hold a local queue and replay it whenever the API becomes reachable, without
needing a merge strategy.

## Scope: three forms, not an app

Most data arrives on its own. Finance imports from Plaid, medical history from C-CDA exports, sleep
from the Withings pad. What still wants typing is:

- **Mood** — daily, usually in bed, which is exactly where a phone wins
- **Nutrition intake and inventory** — at the shop or in the kitchen
- **Body measurements** — occasionally

Building more than these three before knowing which ones actually get used would be guessing.

## Work required

Roughly in order of how much thought each needs.

**Offline queue and sync.** The bulk of the effort, and almost all of it is the service worker and
the replay logic rather than UI. Made tractable by the upsert-by-date schema above.

**An API project.** ASP.NET Core referencing the module assemblies. Running it on the PC works today.
Running it anywhere else first requires splitting each module into a plain `net8.0` `.Data` assembly
and a `net8.0-windows` `.UI` assembly. That split is worth doing on its own merits but is a real
refactor across five modules, so it should not be bundled into a first attempt.

**SQLite concurrency.** The connection string sets no journal mode and no busy timeout, so a second
writer will produce "database is locked". WAL plus a busy timeout fixes it. Small, self-contained,
and worth doing before a second writer exists rather than after — see "cheap standalone item" below.

**Reachability.** The PC may be asleep when an entry gets made. Either host the API on the always-on
box (which requires the module split) or let the phone queue until the PC is awake. The queue is
cheaper and makes uptime mostly irrelevant, so prefer it.

**Authentication and transport.** This writes to a health record, so an unauthenticated LAN endpoint
is not acceptable even at home. Put a device token on the API, and reach it over Tailscale or
WireGuard rather than forwarding a port. Nothing about this data should be exposed to the internet.

## Staged plan

1. **iOS Shortcut posting to a single endpoint** — an afternoon. Siri-triggerable, sits on the home
   screen, needs no app development at all. Its real purpose is to reveal which fields actually get
   reached for on a phone, so stage 3 is designed against evidence instead of guesses.
2. **Read-only PWA** showing today and recent history — a day or two. Proves hosting, auth and
   reachability while risking no writes.
3. **Writable PWA** with the three forms, token auth and the offline queue — a couple of weeks of
   evenings.

Native iOS is stage 3 plus a Mac, an annual fee and a signing pipeline, for no functional gain.

## Cheap standalone item, safe to do any time

Enable WAL and set a busy timeout on the SQLite connection in `App.xaml.cs`. Independent of
everything else here, a few lines, and it removes a failure that would otherwise appear only once a
second writer exists — at which point it would look like an unrelated bug.

## Open decisions, left deliberately

- Split the modules into `.Data` / `.UI`, or accept the API being Windows-only and PC-hosted?
- Tailscale, WireGuard, or LAN-only?
- Is web push wanted at all, or is a home-screen icon enough?
