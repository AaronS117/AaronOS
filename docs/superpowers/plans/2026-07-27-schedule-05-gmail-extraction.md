# Schedule Module — Plan 5: Gmail Scanning and Claude Extraction

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scan Gmail for dated things that never made it onto a calendar, extract them into structured records with the Claude API, and land them in a review queue the user approves before anything joins the schedule.

**Architecture:** `GmailClient` returns subject and snippet only. `MailEventExtractor` sends those to `claude-opus-5` with a JSON schema and gets back a typed result. `MailScanService` orchestrates and writes `InboxItem` rows with status `Pending`. A Review Inbox page turns an accepted item into a `Release`, `ScheduleException`, or `Goal`. Nothing reaches the schedule without an explicit accept.

**Tech Stack:** Adds `Google.Apis.Gmail.v1` 1.74.0 and the `Anthropic` NuGet package (the official Anthropic C# SDK).

**Spec:** `docs/superpowers/specs/2026-07-27-schedule-module-design.md` — this plan covers phase 9.

**Prerequisite:** Plan 4 complete, specifically `GoogleCredentialProvider`, `DpapiDataStore`, and `TokenProtector`, which this plan reuses. 93 tests pass.

## Global Constraints

- Target framework `net8.0-windows`; `LangVersion` `13.0`; `Nullable` `enable`.
- **Never use the partial-property `[ObservableProperty]` form.**
- **Nothing extracted from mail may reach the schedule automatically.** Every extraction lands as `InboxItemStatus.Pending` and requires an explicit user accept. This is not a UX preference — it is the containment for an extractor that will sometimes be wrong.
- **Read-only Gmail.** Request `GmailService.Scope.GmailReadonly` and nothing wider. Never send, label, archive, modify, or delete a message.
- **Send subject and snippet only.** Never fetch or transmit a full message body, attachment, header set, or recipient list to the Claude API. The whole point of using the snippet is that it bounds what leaves the machine.
- The Anthropic API key is DPAPI-protected via `TokenProtector` from Plan 4. **Never** put it in source, a settings file, or an environment variable committed anywhere.
- Every failure is contained: a Gmail error, a Claude error, or a refusal records a message and leaves the queue as it was. Nothing throws into the UI or the background tick.
- Each Gmail message id is extracted at most once — the unique index on `InboxItem.SourceMessageId` enforces it, so re-scanning costs nothing.
- Run tests with `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`.
- **Database-backed tests must write and read through separate `DbContext` instances.** EF Core's
  identity resolution returns already-tracked entities, so a query issued on the same context that
  performed the insert asserts against the objects the test just constructed — not what SQLite
  actually stored. That defeats the entire reason these tests use a real file-backed database: an
  asymmetric value converter, or a nullable `TimeSpan?`/`DateOnly?` that reads back as zero instead
  of null, would pass. Write in one context, dispose it, then verify through a fresh one against the
  same `_dbPath`:

  ```csharp
  await using (var db = CreateContext())
  {
      await db.Database.EnsureCreatedAsync();
      db.Add(/* ... */);
      await db.SaveChangesAsync();
  }

  // Fresh context, same file: the assertions below now check what SQLite stored.
  await using var verify = CreateContext();
  var loaded = await verify.Set<T>().SingleAsync();
  ```

  **This constraint governs the task bodies below.** Where a task's test code still shows a single
  `db` used for both the write and the read, restructure it to the shape above. If an assertion then
  fails, that is a real mapping bug the single-context pattern was hiding — report it; do not adjust
  the assertion to match what you observe.

## Model and cost, stated up front

- **Model:** `claude-opus-5`. Do not substitute a smaller model to save money — that is the user's decision, not the implementer's.
- **Structured output:** `OutputConfig.Format` with a `JsonOutputFormat` schema. The schema is validated at the API layer, so the model retries on a mismatch instead of returning something this module has to defensively parse.
- **Effort:** `Effort.Low`. This is mechanical extraction from two short strings.
- **Thinking:** left at the default, which is **on** for `claude-opus-5`. `MaxTokens` caps thinking plus response together, so leave headroom (2048 is ample for a four-field object). Disabling thinking on this model has documented failure modes — it can emit tool calls as plain text and leak `<thinking>` tags — and is not worth the token saving on calls this small.
- **Refusals:** a declined request returns **HTTP 200** with `StopReason == "refusal"`. Check `StopReason` before reading `Content`; indexing `Content[0]` unconditionally will throw.
- **Cost:** roughly 150 input and 100 output tokens per message at Opus 5's $5 / $25 per million, so about **$0.003 per message scanned** — a few dollars a month at fifty messages a day. The Gmail query is what bounds the bill; keep it narrow.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `src/AaronOS.Modules.Schedule/Data/InboxItem.cs` (+`Configuration`) | One pending extraction awaiting review |
| `src/AaronOS.Modules.Schedule/Data/ScheduleCredentials.cs` (+`Configuration`) | Single-row DPAPI store for the Anthropic key |
| `src/AaronOS.Modules.Schedule/Mail/MailCandidate.cs` | Subject + snippet + id, the only thing that leaves Gmail |
| `src/AaronOS.Modules.Schedule/Mail/GmailClient.cs` | Gmail search returning candidates |
| `src/AaronOS.Modules.Schedule/Mail/ExtractedEvent.cs` | The extractor's typed result |
| `src/AaronOS.Modules.Schedule/Mail/MailEventExtractor.cs` | Claude API call and schema |
| `src/AaronOS.Modules.Schedule/Mail/InboxItemFactory.cs` | Pure mapping from extraction to `InboxItem` |
| `src/AaronOS.Modules.Schedule/Mail/InboxItemAccepter.cs` | Pure decision of what an accepted item becomes |
| `src/AaronOS.Modules.Schedule/Mail/MailScanService.cs` | Orchestration |
| `src/AaronOS.Modules.Schedule/ViewModels/ReviewInboxViewModel.cs` | Review page state |
| `src/AaronOS.Modules.Schedule/Views/ReviewInboxPage.xaml(.cs)` | Accept / dismiss UI |
| `src/AaronOS.Modules.Schedule.Tests/InboxItemFactoryTests.cs` | Mapping and confidence tests |
| `src/AaronOS.Modules.Schedule.Tests/InboxItemAccepterTests.cs` | Accept-conversion tests |

---

## Task 1: InboxItem and credential entities

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Data/InboxItem.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/InboxItemConfiguration.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/ScheduleCredentials.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/ScheduleCredentialsConfiguration.cs`
- Modify: `src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs`

**Interfaces:**
- Consumes: `InboxItemKind`, `InboxItemStatus` from Plan 1's `ScheduleEnums.cs`.
- Produces: `InboxItem` (`Id`, `SourceMessageId`, `DetectedTitle`, `DetectedDate`, `Kind`, `Confidence`, `RawSubject`, `RawSnippet`, `Status`, `CreatedAt`, `ReviewedAt`), `ScheduleCredentials` (`Id`, `EncryptedAnthropicApiKey`, `GmailQuery`).

- [ ] **Step 1: Write the failing test**

Add to `ScheduleSchemaTests`:

```csharp
    [Fact]
    public async Task InboxItem_RejectsADuplicateSourceMessage()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.Add(new InboxItem
        {
            SourceMessageId = "msg-1", DetectedTitle = "Dentist appointment",
            DetectedDate = new DateOnly(2026, 8, 3), Kind = InboxItemKind.Appointment,
            Confidence = 0.92m, RawSubject = "Your appointment is confirmed",
            RawSnippet = "See you on August 3 at 2pm", Status = InboxItemStatus.Pending,
            CreatedAt = new DateTime(2026, 7, 27, 9, 0, 0),
        });
        await db.SaveChangesAsync();

        // Re-scanning must cost nothing: the same message extracted twice is rejected here rather
        // than producing a second review item and a second API charge.
        db.Add(new InboxItem
        {
            SourceMessageId = "msg-1", DetectedTitle = "Duplicate",
            Kind = InboxItemKind.Other, Confidence = 0.5m,
            RawSubject = "x", RawSnippet = "y", Status = InboxItemStatus.Pending,
            CreatedAt = new DateTime(2026, 7, 27, 9, 5, 0),
        });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task InboxItem_AllowsANullDetectedDate()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        // Extraction that found no usable date is still worth keeping — the user may recognise it.
        db.Add(new InboxItem
        {
            SourceMessageId = "msg-2", DetectedTitle = "Something undated",
            DetectedDate = null, Kind = InboxItemKind.Other, Confidence = 0.3m,
            RawSubject = "FYI", RawSnippet = "sometime soon", Status = InboxItemStatus.Pending,
            CreatedAt = new DateTime(2026, 7, 27, 9, 0, 0),
        });
        await db.SaveChangesAsync();

        Assert.Null((await db.Set<InboxItem>().SingleAsync()).DetectedDate);
    }

    [Fact]
    public async Task ScheduleCredentials_StoresAProtectedKeyAndADefaultQuery()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        var credentials = ScheduleCredentials.Default();
        Assert.False(string.IsNullOrWhiteSpace(credentials.GmailQuery));

        db.Add(credentials);
        await db.SaveChangesAsync();

        var loaded = await db.Set<ScheduleCredentials>().SingleAsync();
        Assert.Null(loaded.EncryptedAnthropicApiKey);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `InboxItem` and `ScheduleCredentials`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Data/InboxItem.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// One thing the extractor thinks it found in an email, waiting for the user to accept or dismiss
/// it. The review step is the containment for an extractor that will sometimes be wrong: a false
/// positive costs one dismissal, and nothing reaches the schedule unreviewed.
///
/// The raw subject and snippet are kept so the review page can show what the judgement was based
/// on — a confidence number alone gives the user nothing to check against.
/// </summary>
public class InboxItem
{
    public int Id { get; set; }

    /// <summary>Gmail's message id. Unique, so a re-scan neither duplicates the item nor
    /// re-charges for the extraction.</summary>
    public string SourceMessageId { get; set; } = "";

    public string DetectedTitle { get; set; } = "";

    /// <summary>Null when extraction found no usable date.</summary>
    public DateOnly? DetectedDate { get; set; }

    public InboxItemKind Kind { get; set; }

    /// <summary>0 to 1, as reported by the extractor. Advisory — it orders the queue, it does not
    /// gate anything.</summary>
    public decimal Confidence { get; set; }

    public string RawSubject { get; set; } = "";
    public string RawSnippet { get; set; } = "";
    public InboxItemStatus Status { get; set; } = InboxItemStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
```

`src/AaronOS.Modules.Schedule/Data/InboxItemConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class InboxItemConfiguration : IEntityTypeConfiguration<InboxItem>
{
    public void Configure(EntityTypeBuilder<InboxItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.HasIndex(i => i.SourceMessageId).IsUnique();
        builder.HasIndex(i => i.Status);
        builder.Property(i => i.SourceMessageId).HasMaxLength(200).IsRequired();
        builder.Property(i => i.DetectedTitle).HasMaxLength(300).IsRequired();
        builder.Property(i => i.RawSubject).HasMaxLength(500).IsRequired();
        builder.Property(i => i.RawSnippet).HasMaxLength(2000).IsRequired();
        builder.Property(i => i.Kind).HasConversion<int>();
        builder.Property(i => i.Status).HasConversion<int>();
        builder.Property(i => i.Confidence).HasPrecision(4, 3);
    }
}
```

`src/AaronOS.Modules.Schedule/Data/ScheduleCredentials.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// Single-row store for this module's own secrets and scan configuration, following
/// AaronOS.Modules.Nutrition.Usda.UsdaCredentialStore's shape.
///
/// The API key is DPAPI-protected at rest. It is never in source, never in a config file, and
/// never in an environment variable that could end up committed.
/// </summary>
public class ScheduleCredentials
{
    public int Id { get; set; }

    /// <summary>DPAPI-protected (current-user scope) Anthropic API key. Null until configured, in
    /// which case mail scanning is simply inert.</summary>
    public byte[]? EncryptedAnthropicApiKey { get; set; }

    /// <summary>
    /// The Gmail search that bounds what gets scanned — and therefore what gets billed. Narrow by
    /// default: recent, unread-or-starred mail that looks like it carries a date.
    /// </summary>
    public string GmailQuery { get; set; } =
        "newer_than:7d (appointment OR confirmed OR reservation OR \"order\" OR delivery OR launch OR restock OR \"release date\")";

    public DateTime? LastScannedAt { get; set; }
    public string? LastError { get; set; }

    public static ScheduleCredentials Default() => new();
}
```

`src/AaronOS.Modules.Schedule/Data/ScheduleCredentialsConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class ScheduleCredentialsConfiguration : IEntityTypeConfiguration<ScheduleCredentials>
{
    public void Configure(EntityTypeBuilder<ScheduleCredentials> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.GmailQuery).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.LastError).HasMaxLength(2000);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 96`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Data src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs
git commit -m "Add InboxItem and ScheduleCredentials entities"
```

---

## Task 2: Pure mapping and accept logic

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Mail/ExtractedEvent.cs`
- Create: `src/AaronOS.Modules.Schedule/Mail/MailCandidate.cs`
- Create: `src/AaronOS.Modules.Schedule/Mail/InboxItemFactory.cs`
- Create: `src/AaronOS.Modules.Schedule/Mail/InboxItemAccepter.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/InboxItemFactoryTests.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/InboxItemAccepterTests.cs`

**Interfaces:**
- Produces:
  - `record MailCandidate(string MessageId, string Subject, string Snippet)`
  - `record ExtractedEvent(string Title, DateOnly? Date, InboxItemKind Kind, decimal Confidence)`
  - `static InboxItem? InboxItemFactory.Create(MailCandidate candidate, ExtractedEvent extracted, DateTime now)`
  - `record AcceptResult(Release? Release, ScheduleException? Exception, Goal? Goal)`
  - `static AcceptResult InboxItemAccepter.Accept(InboxItem item, DateTime now)`

Doing the mapping and the accept conversion as pure functions is what makes both testable without a database or an API key — the two places most likely to encode a wrong assumption are also the two easiest to test.

- [ ] **Step 1: Write the failing tests**

Create `src/AaronOS.Modules.Schedule.Tests/InboxItemFactoryTests.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Mail;

namespace AaronOS.Modules.Schedule.Tests;

public class InboxItemFactoryTests
{
    private static readonly DateTime Now = new(2026, 7, 27, 9, 0, 0);

    private static MailCandidate Candidate(string subject = "Your appointment is confirmed") =>
        new("msg-1", subject, "See you on August 3 at 2pm");

    [Fact]
    public void MapsEveryField_AndStartsPending()
    {
        var extracted = new ExtractedEvent("Dentist appointment", new DateOnly(2026, 8, 3), InboxItemKind.Appointment, 0.92m);

        var item = InboxItemFactory.Create(Candidate(), extracted, Now)!;

        Assert.Equal("msg-1", item.SourceMessageId);
        Assert.Equal("Dentist appointment", item.DetectedTitle);
        Assert.Equal(new DateOnly(2026, 8, 3), item.DetectedDate);
        Assert.Equal(InboxItemKind.Appointment, item.Kind);
        Assert.Equal(0.92m, item.Confidence);
        Assert.Equal("Your appointment is confirmed", item.RawSubject);
        Assert.Equal(InboxItemStatus.Pending, item.Status);
        Assert.Equal(Now, item.CreatedAt);
        Assert.Null(item.ReviewedAt);
    }

    [Fact]
    public void RejectsAnExtractionWithNoTitle()
    {
        var extracted = new ExtractedEvent("   ", new DateOnly(2026, 8, 3), InboxItemKind.Appointment, 0.9m);

        // An untitled item gives the reviewer nothing to judge; better to drop it than to queue it.
        Assert.Null(InboxItemFactory.Create(Candidate(), extracted, Now));
    }

    [Fact]
    public void RejectsAnExtractionWithNeitherDateNorUsefulConfidence()
    {
        var extracted = new ExtractedEvent("Something vague", null, InboxItemKind.Other, 0.2m);

        // No date and low confidence is noise. Keep an undated item only when the extractor is
        // actually confident there's something there.
        Assert.Null(InboxItemFactory.Create(Candidate(), extracted, Now));
    }

    [Fact]
    public void KeepsAnUndatedItemWhenConfidenceIsHigh()
    {
        var extracted = new ExtractedEvent("Package arriving soon", null, InboxItemKind.Delivery, 0.85m);

        Assert.NotNull(InboxItemFactory.Create(Candidate(), extracted, Now));
    }

    [Fact]
    public void ClampsConfidenceToTheZeroToOneRange()
    {
        // The model reports this field; nothing guarantees it stays in range.
        Assert.Equal(1m, InboxItemFactory.Create(Candidate(), new ExtractedEvent("A", new DateOnly(2026, 8, 3), InboxItemKind.Other, 4.2m), Now)!.Confidence);
        Assert.Equal(0m, InboxItemFactory.Create(Candidate(), new ExtractedEvent("A", new DateOnly(2026, 8, 3), InboxItemKind.Other, -1m), Now)!.Confidence);
    }

    [Fact]
    public void TruncatesOverlongTextToFitTheColumns()
    {
        var longSubject = new string('x', 900);
        var longSnippet = new string('y', 5000);
        var extracted = new ExtractedEvent(new string('z', 700), new DateOnly(2026, 8, 3), InboxItemKind.Other, 0.9m);

        var item = InboxItemFactory.Create(new MailCandidate("msg-1", longSubject, longSnippet), extracted, Now)!;

        // Column limits are 300 / 500 / 2000. Exceeding them would throw on save, failing the
        // whole scan over one verbose email.
        Assert.Equal(300, item.DetectedTitle.Length);
        Assert.Equal(500, item.RawSubject.Length);
        Assert.Equal(2000, item.RawSnippet.Length);
    }

    [Fact]
    public void RejectsAnImplausiblyDistantDate()
    {
        // A misparsed year ("1/2/26" read as 2126) shouldn't put an item ten decades out.
        var extracted = new ExtractedEvent("Far future", new DateOnly(2126, 1, 2), InboxItemKind.Other, 0.9m);

        Assert.Null(InboxItemFactory.Create(Candidate(), extracted, Now));
    }
}
```

Create `src/AaronOS.Modules.Schedule.Tests/InboxItemAccepterTests.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Mail;

namespace AaronOS.Modules.Schedule.Tests;

public class InboxItemAccepterTests
{
    private static readonly DateTime Now = new(2026, 7, 27, 9, 0, 0);

    private static InboxItem Item(InboxItemKind kind, DateOnly? date = null) => new()
    {
        Id = 1, SourceMessageId = "msg-1", DetectedTitle = "A thing",
        DetectedDate = date ?? new DateOnly(2026, 8, 3), Kind = kind, Confidence = 0.9m,
        RawSubject = "subject", RawSnippet = "snippet", Status = InboxItemStatus.Pending, CreatedAt = Now,
    };

    [Fact]
    public void ReleaseKind_BecomesARelease()
    {
        var result = InboxItemAccepter.Accept(Item(InboxItemKind.Release), Now);

        var release = Assert.IsType<Release>(result.Release);
        Assert.Equal("A thing", release.Title);
        Assert.Equal(new DateOnly(2026, 8, 3), release.ReleaseDate);
        Assert.Equal(ReleaseCategory.Media, release.Category);
        Assert.Null(result.Exception);
        Assert.Null(result.Goal);
    }

    [Fact]
    public void DeliveryKind_BecomesAProductRelease()
    {
        // A delivery is a dated thing to be aware of, which is exactly what Release models —
        // creating a schedule block for a parcel would clutter the agenda for no benefit.
        var result = InboxItemAccepter.Accept(Item(InboxItemKind.Delivery), Now);

        Assert.Equal(ReleaseCategory.Product, result.Release!.Category);
    }

    [Fact]
    public void AppointmentKind_BecomesAStandaloneScheduleException()
    {
        var result = InboxItemAccepter.Accept(Item(InboxItemKind.Appointment), Now);

        var exception = Assert.IsType<ScheduleException>(result.Exception);
        Assert.Equal(new DateOnly(2026, 8, 3), exception.Date);
        Assert.Null(exception.ScheduleBlockId);   // standalone, not a modification of a template block
        Assert.False(exception.IsCancelled);
        Assert.Equal(ScheduleBlockKind.Personal, exception.Kind);
        Assert.Equal("A thing", exception.Label);
        // A time the email didn't state must not be invented; the user sets it on the Week page.
        Assert.NotNull(exception.StartTime);
        Assert.NotNull(exception.EndTime);
        Assert.Contains("msg-1", exception.Note);
        Assert.Null(result.Release);
    }

    [Fact]
    public void DeadlineKind_BecomesAGoal()
    {
        var result = InboxItemAccepter.Accept(Item(InboxItemKind.Deadline), Now);

        var goal = Assert.IsType<Goal>(result.Goal);
        Assert.Equal("A thing", goal.Title);
        Assert.Equal(new DateOnly(2026, 8, 3), goal.TargetDate);
        Assert.Equal(GoalStatus.Active, goal.Status);
        Assert.Equal(Now, goal.CreatedAt);
    }

    [Fact]
    public void OtherKind_BecomesAGoalWithNoTargetDatePressure()
    {
        var result = InboxItemAccepter.Accept(Item(InboxItemKind.Other), Now);

        Assert.NotNull(result.Goal);
        Assert.Null(result.Release);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void UndatedItem_CannotBecomeAnAppointmentOrRelease_AndFallsBackToAGoal()
    {
        // Both of those require a date. A goal doesn't, so an undated item still lands somewhere
        // rather than being silently discarded after the user accepted it.
        var appointment = InboxItemAccepter.Accept(Item(InboxItemKind.Appointment, date: null), Now);
        var release = InboxItemAccepter.Accept(Item(InboxItemKind.Release, date: null), Now);

        Assert.NotNull(appointment.Goal);
        Assert.Null(appointment.Exception);
        Assert.NotNull(release.Goal);
        Assert.Null(release.Release);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `MailCandidate`, `ExtractedEvent`, `InboxItemFactory`, `InboxItemAccepter`, `AcceptResult`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Mail/MailCandidate.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Mail;

/// <summary>
/// The only thing that leaves Gmail: a message id, its subject, and Gmail's own snippet. No body,
/// no attachments, no headers, no recipients — that bound is the point, not an optimisation.
/// </summary>
public sealed record MailCandidate(string MessageId, string Subject, string Snippet);
```

`src/AaronOS.Modules.Schedule/Mail/ExtractedEvent.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Mail;

/// <param name="Date">Null when the message carries no usable date.</param>
/// <param name="Confidence">0 to 1, self-reported by the model. Advisory: it orders the review
/// queue and nothing else. It never gates whether an item is queued.</param>
public sealed record ExtractedEvent(string Title, DateOnly? Date, InboxItemKind Kind, decimal Confidence);
```

`src/AaronOS.Modules.Schedule/Mail/InboxItemFactory.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Mail;

/// <summary>
/// Turns an extraction into a queued review item, or rejects it. Pure, so the filtering rules are
/// testable without an API key (see InboxItemFactoryTests).
///
/// Everything here defends against the model returning something unusable — an empty title, a
/// confidence outside 0-1, a misparsed century, text longer than the columns allow. A scan that
/// threw on one bad extraction would lose the whole batch.
/// </summary>
public static class InboxItemFactory
{
    /// <summary>Keep an undated item only when the extractor is genuinely confident.</summary>
    private const decimal UndatedConfidenceFloor = 0.7m;

    /// <summary>Reject a date more than five years out — almost certainly a misparse.</summary>
    private const int MaxYearsAhead = 5;

    public static InboxItem? Create(MailCandidate candidate, ExtractedEvent extracted, DateTime now)
    {
        var title = extracted.Title?.Trim();
        if (string.IsNullOrEmpty(title)) return null;

        var today = DateOnly.FromDateTime(now);

        if (extracted.Date is { } date)
        {
            if (date > today.AddYears(MaxYearsAhead)) return null;
        }
        else if (extracted.Confidence < UndatedConfidenceFloor)
        {
            return null;
        }

        return new InboxItem
        {
            SourceMessageId = candidate.MessageId,
            DetectedTitle = Truncate(title, 300),
            DetectedDate = extracted.Date,
            Kind = extracted.Kind,
            Confidence = Math.Clamp(extracted.Confidence, 0m, 1m),
            RawSubject = Truncate(candidate.Subject, 500),
            RawSnippet = Truncate(candidate.Snippet, 2000),
            Status = InboxItemStatus.Pending,
            CreatedAt = now,
        };
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= max ? value : value[..max];
    }
}
```

`src/AaronOS.Modules.Schedule/Mail/InboxItemAccepter.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Mail;

/// <summary>Exactly one of these is non-null.</summary>
public sealed record AcceptResult(Release? Release, ScheduleException? Exception, Goal? Goal);

/// <summary>
/// Decides what an accepted review item becomes. Pure, so the routing is testable and visible in
/// one place rather than spread through a ViewModel (see InboxItemAccepterTests).
///
/// A goal is the fallback for anything undated, because both Release and ScheduleException require
/// a date and silently discarding something the user just accepted would be the worst outcome.
/// </summary>
public static class InboxItemAccepter
{
    /// <summary>A placeholder duration for an appointment whose time the email didn't state. The
    /// user adjusts it on the Week page; inventing a specific time would be a guess presented as
    /// fact.</summary>
    private static readonly TimeSpan DefaultStart = new(9, 0, 0);
    private static readonly TimeSpan DefaultEnd = new(10, 0, 0);

    public static AcceptResult Accept(InboxItem item, DateTime now)
    {
        if (item.DetectedDate is not { } date)
        {
            return new AcceptResult(null, null, BuildGoal(item, null, now));
        }

        return item.Kind switch
        {
            InboxItemKind.Release => new AcceptResult(BuildRelease(item, date, ReleaseCategory.Media), null, null),
            InboxItemKind.Delivery => new AcceptResult(BuildRelease(item, date, ReleaseCategory.Product), null, null),
            InboxItemKind.Appointment => new AcceptResult(null, BuildException(item, date), null),
            _ => new AcceptResult(null, null, BuildGoal(item, date, now)),
        };
    }

    private static Release BuildRelease(InboxItem item, DateOnly date, ReleaseCategory category) => new()
    {
        Title = item.DetectedTitle,
        Category = category,
        ReleaseDate = date,
        // The date came from prose, so treat it as an estimate until the user says otherwise.
        IsDateEstimated = true,
        Notes = $"From email: {item.RawSubject}",
    };

    private static ScheduleException BuildException(InboxItem item, DateOnly date) => new()
    {
        Date = date,
        ScheduleBlockId = null, // standalone, not a modification of a template block
        IsCancelled = false,
        Kind = ScheduleBlockKind.Personal,
        Label = item.DetectedTitle,
        StartTime = DefaultStart,
        EndTime = DefaultEnd,
        // The message id makes it possible to trace an agenda entry back to the email it came from.
        Note = $"From email {item.SourceMessageId}: {item.RawSubject}",
    };

    private static Goal BuildGoal(InboxItem item, DateOnly? date, DateTime now) => new()
    {
        Title = item.DetectedTitle,
        Description = $"From email: {item.RawSubject}",
        TargetDate = date,
        Status = GoalStatus.Active,
        CreatedAt = now,
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 109`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Mail src/AaronOS.Modules.Schedule.Tests
git commit -m "Add pure inbox item mapping and accept-conversion logic"
```

---

## Task 3: Gmail client

**Files:**
- Modify: `src/AaronOS.Modules.Schedule/AaronOS.Modules.Schedule.csproj`
- Create: `src/AaronOS.Modules.Schedule/Mail/GmailClient.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Consumes: `GoogleCredentialProvider` from Plan 4.
- Produces: `GmailClient` with `Task<IReadOnlyList<MailCandidate>> SearchAsync(int googleCalendarId, string query, int maxMessages, CancellationToken ct)` and `static readonly string[] Scopes`.

- [ ] **Step 1: Add the package**

```xml
    <PackageReference Include="Google.Apis.Gmail.v1" Version="1.74.0.4162" />
```

- [ ] **Step 2: Write the client**

No unit test: every line is a call into the Gmail SDK, and asserting a mocked SDK would test the mock. The extraction and mapping around it are covered by 13 pure tests, and this is verified end to end in Task 6.

```csharp
using AaronOS.Modules.Schedule.External;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;

namespace AaronOS.Modules.Schedule.Mail;

/// <summary>
/// Searches Gmail and returns subject plus snippet only.
///
/// Two deliberate limits. The scope is GmailReadonly, so this credential cannot send, label,
/// archive, or delete even if a future change tried to. And the metadata format returns headers and
/// Gmail's own snippet without the body — so no message body, attachment, or recipient list ever
/// leaves the machine, which is what bounds both the privacy exposure and the extraction cost.
/// </summary>
public sealed class GmailClient(GoogleCredentialProvider credentialProvider)
{
    public static readonly string[] Scopes = [GmailService.Scope.GmailReadonly];

    public async Task<IReadOnlyList<MailCandidate>> SearchAsync(
        int googleCalendarId,
        string query,
        int maxMessages,
        CancellationToken cancellationToken)
    {
        // Reuses the calendar's stored credential row, so connecting Google once covers both
        // Calendar and Gmail — provided both scopes were granted (see Task 6).
        var credential = await credentialProvider.AuthorizeAsync(
            googleCalendarId,
            External.GoogleCalendarClient.Scopes.Concat(Scopes),
            cancellationToken);

        using var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AaronOS",
        });

        var listRequest = service.Users.Messages.List("me");
        listRequest.Q = query;
        listRequest.MaxResults = maxMessages;

        var listResponse = await listRequest.ExecuteAsync(cancellationToken);
        if (listResponse.Messages is null) return [];

        var candidates = new List<MailCandidate>();

        foreach (var summary in listResponse.Messages.Take(maxMessages))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var getRequest = service.Users.Messages.Get("me", summary.Id);
            // Metadata only: headers plus the snippet, never the body.
            getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            getRequest.MetadataHeaders = ["Subject"];

            var message = await getRequest.ExecuteAsync(cancellationToken);

            var subject = message.Payload?.Headers
                ?.FirstOrDefault(h => string.Equals(h.Name, "Subject", StringComparison.OrdinalIgnoreCase))
                ?.Value ?? "(no subject)";

            candidates.Add(new MailCandidate(summary.Id, subject, message.Snippet ?? ""));
        }

        return candidates;
    }
}
```

Confirm `UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata` and `MetadataHeaders` against the installed package; if a name differs the compiler will say so. Do **not** switch to `FormatEnum.Full` to make it compile — that fetches message bodies, which violates this plan's constraints.

- [ ] **Step 3: Register it**

In `ScheduleModule.RegisterServices`:

```csharp
        services.AddSingleton<GmailClient>();
```

with `using AaronOS.Modules.Schedule.Mail;`.

- [ ] **Step 4: Verify it compiles and tests still pass**

Run: `dotnet build AaronOS.slnx --nologo`
Expected: `Build succeeded`.

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 109`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add read-only Gmail client returning subject and snippet only"
```

---

## Task 4: Claude extractor

**Files:**
- Modify: `src/AaronOS.Modules.Schedule/AaronOS.Modules.Schedule.csproj`
- Create: `src/AaronOS.Modules.Schedule/Mail/MailEventExtractor.cs`
- Create: `src/AaronOS.Modules.Schedule/Mail/ScheduleCredentialStore.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Consumes: `MailCandidate`, `ExtractedEvent`, `TokenProtector` (Plan 4), `ScheduleCredentials`.
- Produces: `ScheduleCredentialStore` with `Task<string?> GetAnthropicApiKeyAsync()` and `Task SetAnthropicApiKeyAsync(string)`; `MailEventExtractor` with `Task<ExtractedEvent?> ExtractAsync(MailCandidate candidate, DateOnly today, CancellationToken ct)`.

- [ ] **Step 1: Add the package**

```xml
    <PackageReference Include="Anthropic" Version="*" />
```

Pin the version NuGet resolves rather than leaving the wildcard: run `dotnet add package Anthropic --project src/AaronOS.Modules.Schedule/AaronOS.Modules.Schedule.csproj` and let it write the concrete version.

- [ ] **Step 2: Write the credential store**

```csharp
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.External;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.Mail;

/// <summary>
/// DPAPI-protected storage for this module's Anthropic API key, following
/// AaronOS.Modules.Nutrition.Usda.UsdaCredentialStore. The key is never in source, never in a
/// config file, and never in plaintext at rest.
/// </summary>
public sealed class ScheduleCredentialStore(IDbContextFactory<AaronOsDbContext> dbContextFactory)
{
    public async Task<ScheduleCredentials> GetOrCreateAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var credentials = await db.Set<ScheduleCredentials>().FirstOrDefaultAsync();
        if (credentials is not null) return credentials;

        credentials = ScheduleCredentials.Default();
        db.Add(credentials);
        await db.SaveChangesAsync();
        return credentials;
    }

    /// <summary>Null when no key is configured, in which case mail scanning is simply inert.</summary>
    public async Task<string?> GetAnthropicApiKeyAsync()
    {
        var credentials = await GetOrCreateAsync();
        if (credentials.EncryptedAnthropicApiKey is not { Length: > 0 } encrypted) return null;

        try
        {
            return TokenProtector.Unprotect(encrypted);
        }
        catch (Exception)
        {
            // Undecryptable: a copied database or a different Windows account. Treat as
            // unconfigured so the user is prompted to re-enter, rather than throwing from a read.
            return null;
        }
    }

    public async Task SetAnthropicApiKeyAsync(string apiKey)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var credentials = await db.Set<ScheduleCredentials>().FirstOrDefaultAsync();
        if (credentials is null)
        {
            credentials = ScheduleCredentials.Default();
            db.Add(credentials);
        }

        credentials.EncryptedAnthropicApiKey = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : TokenProtector.Protect(apiKey.Trim());

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 3: Write the extractor**

```csharp
using System.Diagnostics;
using System.Text.Json;
using AaronOS.Modules.Schedule.Data;
using Anthropic;
using Anthropic.Models.Messages;

namespace AaronOS.Modules.Schedule.Mail;

/// <summary>
/// Turns an email's subject and snippet into a structured event using the Claude API.
///
/// Uses structured outputs rather than parsing free text: the schema is validated at the API layer,
/// so the model retries on a mismatch instead of this module having to defensively parse whatever
/// came back. Effort is Low because this is mechanical extraction from two short strings; thinking
/// is left at its default (on for claude-opus-5) with MaxTokens headroom, since MaxTokens caps
/// thinking and response together and disabling thinking on this model has known failure modes.
///
/// Roughly 150 input and 100 output tokens per call — about $0.003 per message at Opus 5 rates.
/// The Gmail query is what bounds the bill.
/// </summary>
public sealed class MailEventExtractor(ScheduleCredentialStore credentialStore)
{
    private const string ModelId = "claude-opus-5";

    /// <summary>Ample for a four-field object plus thinking headroom.</summary>
    private const int MaxTokens = 2048;

    /// <returns>Null when no API key is configured, the model declined, or the response could not
    /// be read. Never throws for those cases — a failed extraction skips one message.</returns>
    public async Task<ExtractedEvent?> ExtractAsync(
        MailCandidate candidate,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var apiKey = await credentialStore.GetAnthropicApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var client = new AnthropicClient { ApiKey = apiKey };

        var parameters = new MessageCreateParams
        {
            Model = ModelId,
            MaxTokens = MaxTokens,
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Low,
                Format = new JsonOutputFormat { Schema = BuildSchema() },
            },
            System = BuildSystemPrompt(today),
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    // Subject and snippet only — never a body, attachment, or recipient list.
                    Content = $"Subject: {candidate.Subject}\nSnippet: {candidate.Snippet}",
                },
            ],
        };

        Message response;
        try
        {
            response = await client.Messages.Create(parameters, cancellationToken);
        }
        catch (Exception ex)
        {
            // A rate limit, a network failure, an invalid key. One message is skipped; the scan
            // continues, and the caller records the message.
            Debug.WriteLine($"MailEventExtractor: request failed for {candidate.MessageId}: {ex.Message}");
            return null;
        }

        // A refused request returns HTTP 200 with StopReason "refusal" — reading Content
        // unconditionally would throw. Check first.
        if (response.StopReason == "refusal")
        {
            Debug.WriteLine($"MailEventExtractor: refused for {candidate.MessageId}: {response.StopDetails?.Explanation}");
            return null;
        }

        var json = response.Content
            .Select(block => block.Value)
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .FirstOrDefault();

        return json is null ? null : Parse(json);
    }

    private static string BuildSystemPrompt(DateOnly today) =>
        $"""
        You extract a single upcoming dated item from an email's subject and snippet.

        Today is {today:yyyy-MM-dd}. Resolve relative dates ("next Tuesday", "in 3 weeks") against it.

        Rules:
        - title: a short noun phrase naming the thing, not a restatement of the subject line.
        - date: ISO 8601 (YYYY-MM-DD), or null if the text gives no usable date. Do not guess a
          date that is not supported by the text.
        - kind: Appointment (a scheduled commitment at a place or time), Delivery (a parcel or
          order arriving), Release (a game, film, show, or product becoming available),
          Deadline (something the reader must act on by a date), or Other.
        - confidence: 0 to 1, how sure you are that this email really carries an upcoming dated
          item worth tracking. Marketing mail with no specific commitment should score low.

        Return only the JSON object.
        """;

    /// <summary>
    /// The output schema. Kind is a string enum matching InboxItemKind's member names, so the
    /// mapping below is a plain Enum.TryParse with no lookup table to drift.
    /// </summary>
    private static Dictionary<string, JsonElement> BuildSchema() => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            title = new { type = "string" },
            date = new { type = new[] { "string", "null" } },
            kind = new { type = "string", @enum = Enum.GetNames<InboxItemKind>() },
            confidence = new { type = "number" },
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "title", "date", "kind", "confidence" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };

    private static ExtractedEvent? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var title = root.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) return null;

            DateOnly? date = null;
            if (root.TryGetProperty("date", out var dateElement)
                && dateElement.ValueKind == JsonValueKind.String
                && DateOnly.TryParse(dateElement.GetString(), out var parsedDate))
            {
                date = parsedDate;
            }

            var kind = InboxItemKind.Other;
            if (root.TryGetProperty("kind", out var kindElement)
                && Enum.TryParse<InboxItemKind>(kindElement.GetString(), ignoreCase: true, out var parsedKind))
            {
                kind = parsedKind;
            }

            var confidence = root.TryGetProperty("confidence", out var confidenceElement)
                && confidenceElement.ValueKind == JsonValueKind.Number
                    ? confidenceElement.GetDecimal()
                    : 0m;

            return new ExtractedEvent(title.Trim(), date, kind, confidence);
        }
        catch (JsonException ex)
        {
            // The schema makes this very unlikely, but a parse failure must skip one message
            // rather than fail the scan.
            Debug.WriteLine($"MailEventExtractor: unparseable response: {ex.Message}");
            return null;
        }
    }
}
```

**Reconcile against the installed SDK.** `AnthropicClient`, `MessageCreateParams`, `OutputConfig`, `JsonOutputFormat`, `Effort.Low`, `Role.User`, and `TextBlock` are the documented C# shapes, and `ContentBlock` is a union unwrapped via `.Value` then `OfType<TextBlock>()`. If a member name differs, locate the real one without guessing:

```bash
strings ~/.nuget/packages/anthropic/*/lib/*/Anthropic.dll | grep -iE 'outputconfig|jsonoutputformat|stopdetails' | sort -u | head -30
```

Then write it and let the compiler point at the mismatch. Do not change the model id, the effort level, or the thinking configuration to make something compile.

- [ ] **Step 4: Register both**

```csharp
        services.AddSingleton<ScheduleCredentialStore>();
        services.AddSingleton<MailEventExtractor>();
```

- [ ] **Step 5: Verify and commit**

Run: `dotnet build AaronOS.slnx --nologo`
Expected: `Build succeeded`.

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 109`

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add Claude-based mail event extractor with DPAPI-stored API key"
```

---

## Task 5: Scan orchestration and the Review Inbox page

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Mail/MailScanService.cs`
- Create: `src/AaronOS.Modules.Schedule/ViewModels/ReviewInboxViewModel.cs`
- Create: `src/AaronOS.Modules.Schedule/Views/ReviewInboxPage.xaml`
- Create: `src/AaronOS.Modules.Schedule/Views/ReviewInboxPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml(.cs)`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleSettingsSection.xaml(.cs)`
- Modify: `src/AaronOS.Modules.Schedule/ViewModels/ScheduleSettingsViewModel.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Produces: `MailScanService` with `Task<int> ScanAsync(CancellationToken ct)` returning items queued; `ReviewInboxViewModel` with `ObservableCollection<InboxItem> PendingItems`, `LoadCommand`, `ScanCommand`, `AcceptCommand`, `DismissCommand`. `ScheduleSettingsViewModel` gains `SaveApiKeyCommand` and `SaveGmailQueryCommand`.

- [ ] **Step 1: Write the scan service**

```csharp
using System.Diagnostics;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Data;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.Mail;

/// <summary>
/// Search Gmail, extract each candidate, queue what survives. Every step is contained: a failure
/// on one message skips that message, and a failure of the whole scan records a message and leaves
/// the existing queue untouched.
/// </summary>
public sealed class MailScanService(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    GmailClient gmailClient,
    MailEventExtractor extractor,
    ScheduleCredentialStore credentialStore)
{
    /// <summary>Hard ceiling per scan. At roughly $0.003 per message this bounds a single scan to
    /// about fifteen cents, which matters because the scan can be triggered repeatedly.</summary>
    private const int MaxMessagesPerScan = 50;

    /// <returns>How many new review items were queued.</returns>
    public async Task<int> ScanAsync(CancellationToken cancellationToken)
    {
        var credentials = await credentialStore.GetOrCreateAsync();

        if (await credentialStore.GetAnthropicApiKeyAsync() is null)
        {
            await RecordAsync("No Anthropic API key configured — mail scanning is off.", cancellationToken);
            return 0;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var googleCalendar = await db.Set<ExternalCalendar>()
            .FirstOrDefaultAsync(c => c.Provider == CalendarProvider.GoogleCalendar, cancellationToken);

        if (googleCalendar is null)
        {
            await RecordAsync("Connect Google Calendar first — Gmail reuses that credential.", cancellationToken);
            return 0;
        }

        IReadOnlyList<MailCandidate> candidates;
        try
        {
            candidates = await gmailClient.SearchAsync(
                googleCalendar.Id, credentials.GmailQuery, MaxMessagesPerScan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RecordAsync($"Gmail search failed: {ex.Message}", cancellationToken);
            return 0;
        }

        // Skip anything already extracted: the unique index would reject it anyway, and skipping
        // early avoids paying for an extraction whose result is discarded.
        var seenIds = await db.Set<InboxItem>()
            .Select(i => i.SourceMessageId)
            .ToListAsync(cancellationToken);
        var seen = seenIds.ToHashSet();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var queued = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seen.Contains(candidate.MessageId)) continue;

            var extracted = await extractor.ExtractAsync(candidate, today, cancellationToken);
            if (extracted is null) continue; // no key, refusal, or transport failure — skip one

            var item = InboxItemFactory.Create(candidate, extracted, DateTime.Now);
            if (item is null) continue; // rejected as unusable

            db.Add(item);
            seen.Add(candidate.MessageId);
            queued++;
        }

        try
        {
            var fresh = await db.Set<ScheduleCredentials>().FirstAsync(cancellationToken);
            fresh.LastScannedAt = DateTime.Now;
            fresh.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await RecordAsync($"Could not save scan results: {ex.Message}", cancellationToken);
            return 0;
        }

        return queued;
    }

    private async Task RecordAsync(string message, CancellationToken cancellationToken)
    {
        Debug.WriteLine($"MailScanService: {message}");

        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var credentials = await db.Set<ScheduleCredentials>().FirstOrDefaultAsync(cancellationToken);
            if (credentials is null) return;

            credentials.LastError = message.Length > 2000 ? message[..2000] : message;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MailScanService: could not record error: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Write the Review Inbox ViewModel**

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Mail;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

public partial class ReviewInboxViewModel(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    MailScanService scanService) : ViewModelBase
{
    public ObservableCollection<InboxItem> PendingItems { get; } = [];

    [ObservableProperty]
    private string? _statusMessage;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            var items = await db.Set<InboxItem>()
                .Where(i => i.Status == InboxItemStatus.Pending)
                .ToListAsync();

            PendingItems.Clear();
            // Highest confidence first: the reviewer's attention is the scarce resource.
            foreach (var item in items.OrderByDescending(i => i.Confidence).ThenBy(i => i.DetectedDate))
            {
                PendingItems.Add(item);
            }

            var credentials = await db.Set<ScheduleCredentials>().FirstOrDefaultAsync();
            StatusMessage = credentials?.LastError
                ?? (credentials?.LastScannedAt is { } at ? $"Last scanned {at:g}." : "Not scanned yet.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusMessage = "Scanning mail…";
        try
        {
            var queued = await scanService.ScanAsync(CancellationToken.None);
            await LoadAsync();

            // LoadAsync sets StatusMessage from LastError when there is one; only overwrite it
            // with a success message if the scan actually succeeded.
            if (StatusMessage is null || StatusMessage.StartsWith("Last scanned", StringComparison.Ordinal))
            {
                StatusMessage = queued == 0 ? "Nothing new found." : $"{queued} item(s) queued for review.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Converts the item into a real record and marks it accepted. This is the only path
    /// by which anything extracted from mail reaches the schedule.</summary>
    [RelayCommand]
    private async Task AcceptAsync(InboxItem item)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var tracked = await db.Set<InboxItem>().SingleAsync(i => i.Id == item.Id);
        var result = InboxItemAccepter.Accept(tracked, DateTime.Now);

        if (result.Release is { } release) db.Add(release);
        if (result.Exception is { } exception) db.Add(exception);
        if (result.Goal is { } goal) db.Add(goal);

        tracked.Status = InboxItemStatus.Accepted;
        tracked.ReviewedAt = DateTime.Now;
        await db.SaveChangesAsync();

        await LoadAsync();
        StatusMessage = $"Accepted “{tracked.DetectedTitle}”.";
    }

    [RelayCommand]
    private async Task DismissAsync(InboxItem item)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var tracked = await db.Set<InboxItem>().SingleAsync(i => i.Id == item.Id);
        // Dismissed, not deleted: the row keeps the message id, so a re-scan won't re-queue and
        // re-charge for something already rejected.
        tracked.Status = InboxItemStatus.Dismissed;
        tracked.ReviewedAt = DateTime.Now;
        await db.SaveChangesAsync();

        await LoadAsync();
    }
}
```

- [ ] **Step 3: Write the page**

`src/AaronOS.Modules.Schedule/Views/ReviewInboxPage.xaml`:

```xml
<Page
    x:Class="AaronOS.Modules.Schedule.Views.ReviewInboxPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    mc:Ignorable="d">

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="16">
            <ui:TextBlock Text="Review inbox" FontTypography="Subtitle" Margin="0,0,0,4" />
            <TextBlock TextWrapping="Wrap" Opacity="0.75" Margin="0,0,0,8"
                       Text="Things found in your email that aren't on a calendar. Nothing here affects your schedule until you accept it." />

            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                <ui:Button Content="Scan mail now" Appearance="Primary" Command="{Binding ScanCommand}" Margin="0,0,8,0" />
                <ui:TextBlock Text="{Binding StatusMessage}" VerticalAlignment="Center" TextWrapping="Wrap" />
            </StackPanel>

            <ItemsControl ItemsSource="{Binding PendingItems}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <ui:Card Margin="0,0,0,8">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0">
                                    <ui:TextBlock Text="{Binding DetectedTitle}" FontTypography="BodyStrong" />
                                    <TextBlock>
                                        <Run Text="{Binding Kind}" />
                                        <Run Text="{Binding DetectedDate, StringFormat=' · {0:ddd MMM d, yyyy}', TargetNullValue=' · no date found'}" />
                                        <Run Text=" · confidence " /><Run Text="{Binding Confidence, StringFormat='{}{0:P0}'}" />
                                    </TextBlock>
                                    <TextBlock Text="{Binding RawSubject}" Opacity="0.75" TextWrapping="Wrap" Margin="0,4,0,0" />
                                    <TextBlock Text="{Binding RawSnippet}" Opacity="0.6" TextWrapping="Wrap" MaxHeight="60" />
                                </StackPanel>
                                <ui:Button Grid.Column="1" Content="Accept" Appearance="Primary" Click="Accept_Click" Margin="0,0,8,0" />
                                <ui:Button Grid.Column="2" Content="Dismiss" Click="Dismiss_Click" />
                            </Grid>
                        </ui:Card>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </ScrollViewer>
</Page>
```

The raw subject and snippet are shown on every card deliberately: a confidence percentage alone gives the reviewer nothing to check the extraction against.

`src/AaronOS.Modules.Schedule/Views/ReviewInboxPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule.Views;

public sealed partial class ReviewInboxPage : Page
{
    public ReviewInboxViewModel ViewModel { get; }

    public ReviewInboxPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<ReviewInboxViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InboxItem item })
        {
            _ = ViewModel.AcceptCommand.ExecuteAsync(item);
        }
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InboxItem item })
        {
            _ = ViewModel.DismissCommand.ExecuteAsync(item);
        }
    }
}
```

- [ ] **Step 4: Add the API key and query fields to Settings**

In `ScheduleSettingsViewModel`, add the store to the constructor parameters (`ScheduleCredentialStore credentialStore`) plus:

```csharp
    [ObservableProperty]
    private string _anthropicApiKey = "";

    [ObservableProperty]
    private string _gmailQuery = "";

    [RelayCommand]
    private async Task SaveApiKeyAsync()
    {
        await credentialStore.SetAnthropicApiKeyAsync(AnthropicApiKey);
        // Never keep the key in a bound property longer than needed.
        AnthropicApiKey = "";
        StatusMessage = "API key saved.";
    }

    [RelayCommand]
    private async Task SaveGmailQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(GmailQuery))
        {
            StatusMessage = "The Gmail query can't be empty — it's what bounds what gets scanned.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var credentials = await db.Set<ScheduleCredentials>().FirstOrDefaultAsync();
        if (credentials is null)
        {
            credentials = ScheduleCredentials.Default();
            db.Add(credentials);
        }
        credentials.GmailQuery = GmailQuery.Trim();
        await db.SaveChangesAsync();

        StatusMessage = "Gmail query saved.";
    }
```

In `LoadAsync`, populate the query field (and never the key — a write-only field):

```csharp
            var credentials = await db.Set<ScheduleCredentials>().FirstOrDefaultAsync();
            GmailQuery = credentials?.GmailQuery ?? ScheduleCredentials.Default().GmailQuery;
```

Add to `ScheduleSettingsSection.xaml`, after the calendars block:

```xml
        <ui:TextBlock Text="Mail scanning" FontTypography="BodyStrong" Margin="0,16,0,4" />
        <TextBlock TextWrapping="Wrap" Opacity="0.75" Margin="0,0,0,8"
                   Text="Sends only each message's subject and snippet to the Claude API — never the body. Roughly $0.003 per message scanned. Findings go to the review inbox; nothing reaches your schedule until you accept it." />
        <PasswordBox x:Name="ApiKeyBox" PasswordChanged="ApiKeyBox_PasswordChanged" Margin="0,0,0,8" />
        <ui:Button Content="Save API key" Command="{Binding SaveApiKeyCommand}" HorizontalAlignment="Left" Margin="0,0,0,8" />
        <ui:TextBox PlaceholderText="Gmail search query" Text="{Binding GmailQuery, Mode=TwoWay}" Margin="0,0,0,8" />
        <ui:Button Content="Save query" Command="{Binding SaveGmailQueryCommand}" HorizontalAlignment="Left" />
```

A `PasswordBox` rather than a `ui:TextBox` so the key is not displayed. `PasswordBox.Password` is deliberately not bindable, so push it in code-behind — in `ScheduleSettingsSection.xaml.cs`:

```csharp
    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // PasswordBox.Password isn't a DependencyProperty (by design — binding it would put the
        // secret in the binding engine's diagnostics), so push it across manually.
        ViewModel.AnthropicApiKey = ApiKeyBox.Password;
    }
```

Clear the box after a successful save by adding `ApiKeyBox.Clear();` to a small handler, or leave it — the ViewModel already blanks its own copy.

- [ ] **Step 5: Register and add the shell button**

```csharp
        services.AddSingleton<MailScanService>();
        services.AddTransient<ReviewInboxViewModel>();
```

In `ScheduleShellPage.xaml`, add (giving the previous last button a right margin):

```xml
            <ui:Button Content="Inbox" Click="Inbox_Click" />
```

and in the code-behind:

```csharp
    private void Inbox_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new ReviewInboxPage());
```

- [ ] **Step 6: Verify it compiles and tests pass**

Run: `dotnet build AaronOS.slnx --nologo`
Expected: `Build succeeded`.

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 109`

- [ ] **Step 7: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add mail scan orchestration and Review Inbox page"
```

---

## Task 6: End-to-end verification

**Files:** none. Everything in Tasks 3–5 crosses a network boundary and cannot be established by a unit test.

- [ ] **Step 1: Re-grant Google consent for the Gmail scope**

The credential stored in Plan 4 covers Calendar only. `GmailClient` requests Calendar **and** Gmail scopes, so consent must be granted again.

1. Run the app with the client secret variable set:
   ```bash
   export AARONOS_GOOGLE_CLIENT_SECRET="/c/Users/aaron/secrets/google-oauth-desktop.json"
   dotnet run --project src/AaronOS.App/AaronOS.App.csproj
   ```
2. In your Google Cloud project, confirm the **Gmail API** is enabled alongside the Calendar API. If it is not, enable it — otherwise the scan fails with a "has not been used in project" error.
3. Settings → **Connect Google Calendar**. The consent screen now lists both calendar and mail read access. Grant it.

Expected: sync still succeeds afterwards, confirming the widened scope did not break the calendar path.

- [ ] **Step 2: Configure the key and run a scan**

1. Settings → Mail scanning. Paste an Anthropic API key into the password box and click **Save API key**.
2. Confirm the key is not stored in plaintext:
   ```bash
   sqlite3 "$LOCALAPPDATA/AaronOS/aaronos.db" \
     "SELECT length(EncryptedAnthropicApiKey) > 0, instr(CAST(EncryptedAnthropicApiKey AS TEXT), 'sk-ant') FROM ScheduleCredentials;"
   ```
   Expected: `1|0` — a key is stored and the literal `sk-ant` prefix does not appear in it.
3. Schedule → **Inbox** → **Scan mail now**.

Expected: the status reports a number of items queued (or "Nothing new found"), and cards appear showing a title, kind, date, confidence, and the subject and snippet they were derived from.

- [ ] **Step 3: Check the extraction quality against reality**

For each queued card, compare the detected title, date, and kind against the subject and snippet shown beneath it.

Expected: dates resolve correctly, including relative ones. Marketing mail with no specific commitment scores low confidence and sorts to the bottom. This is a judgement call, not a pass/fail: note what it gets wrong. If dates are systematically off by a day, the system prompt's "today is" line is likely being computed in the wrong direction — check it before adjusting the prompt.

- [ ] **Step 4: Verify accept conversion for each kind**

Accept one item of each kind that appeared and confirm where it landed:

- **Release or Delivery** → Goals & Releases page, in the Releases list, marked as an estimated date.
- **Appointment** → Week page, on its date, as a standalone entry at 09:00–10:00 with a note naming the source message.
- **Deadline or Other** → Goals page, as an active goal with the detected date as its target.

Then confirm the item disappeared from the inbox and did not come back after navigating away and returning.

- [ ] **Step 5: Verify dismissal and re-scan behaviour**

1. Dismiss a remaining item. It disappears.
2. Click **Scan mail now** again.

Expected: the dismissed item does **not** reappear, and no second extraction is charged for it — the row survives with status `Dismissed` and its message id is skipped. Confirm with:

```bash
sqlite3 "$LOCALAPPDATA/AaronOS/aaronos.db" "SELECT Status, COUNT(*) FROM InboxItems GROUP BY Status;"
```

Run it before and after the second scan; dismissed and accepted counts must not change.

- [ ] **Step 6: Verify the failure paths**

Each of these must leave the app running with an explanatory status and no dialog:

1. **No API key.** Clear it: `sqlite3 "$LOCALAPPDATA/AaronOS/aaronos.db" "UPDATE ScheduleCredentials SET EncryptedAnthropicApiKey = NULL;"` Relaunch and scan. Expected: "No Anthropic API key configured — mail scanning is off." No Gmail call is made and nothing is charged.
2. **Invalid API key.** Save `sk-ant-invalid` and scan. Expected: the scan completes having queued nothing; the extractor logs per-message failures to the debug output rather than throwing.
3. **No Google connection.** Remove the Google calendar row in Settings and scan. Expected: "Connect Google Calendar first — Gmail reuses that credential."
4. **Empty query.** Clear the Gmail query field and click **Save query**. Expected: the message explaining the query bounds what gets scanned, and the stored query unchanged.

Restore a valid key and the Google connection afterwards.

- [ ] **Step 7: Confirm the privacy boundary holds**

This is the assertion most worth verifying directly rather than trusting. Attach a debugger or add a temporary breakpoint in `MailEventExtractor.ExtractAsync` and inspect the `Content` string being sent.

Expected: it contains exactly the subject line and Gmail's snippet, and nothing else — no body text, no recipient addresses, no headers beyond the subject. Remove any temporary instrumentation before committing.

- [ ] **Step 8: Record the outcome**

No code changed unless a step surfaced a fix. Commit any fix with a message naming what the verification caught, and note the observed extraction quality — it is the main input to deciding whether the prompt or the Gmail query needs tuning later.

---

## Definition of done for Plan 5

- `dotnet build AaronOS.slnx --nologo` succeeds.
- `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo` reports 109 passing tests, 0 failing.
- A scan queues items with a title, date, kind, and confidence, each showing the subject and snippet it came from.
- Accepting produces the right record type for each kind; dismissing prevents re-queuing.
- Only subject and snippet leave the machine, verified in Task 6 Step 7.
- The Anthropic key and the Google token are both encrypted at rest; no secret and no client-secret file is committed.
- Every failure path leaves the app running with an explanatory status, never a dialog or a crash.

## The whole module is now complete

All nine spec phases are implemented across Plans 1–5. Deliberately not built, recorded so the next person does not assume an oversight:

- Writing to any external calendar. Read-only throughout, by design.
- Microsoft Graph as an Outlook transport. `IExternalCalendarSource` is the seam; whether it is needed depends on Plan 4's Task 0 gate.
- Notifications while the app is closed. Needs a Windows Scheduled Task, out of scope per the spec.
- Wearable or phone sleep import. `SleepLog` is shaped for a backfilling importer, but none exists.
- Actionable toast buttons. Needs `Microsoft.Toolkit.Uwp.Notifications` plus an AUMID shortcut and COM activator.
- A weekday picker in the routine editor; editing an existing goal's title or target date; reordering milestones; selecting a non-primary Google calendar. All supported by the entities, none exposed in the UI.
