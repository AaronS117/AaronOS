# Medical Module — Design

## Context

AaronOS is a modular WPF desktop app (see `docs/MODULE_GUIDELINES.md`). It currently has three
modules: `BodyMeasurements`, `Finance`, and `Nutrition`. The user wants a fourth to hold their
medical history, and asked how hard it would be to pull that history out of MyChart automatically.

### On MyChart, and why the design lands where it does

MyChart is Epic's patient portal. Epic exposes FHIR R4 APIs and supports SMART-on-FHIR OAuth 2.0,
including public/native clients with PKCE — which would fit a desktop app using a loopback redirect.
US regulation (the 21st Century Cures Act rules) requires certified EHRs to offer patients API
access to their own records, so a patient-facing read integration is a supported pathway in
principle, covering `Condition`, `MedicationRequest`, `AllergyIntolerance`, `Immunization`,
`Observation`, `Procedure` and `DocumentReference`.

The blocker is not the code. A developer registration yields a **non-production** client ID that
only reaches Epic's sandbox. Reaching real records at a real health system needs a production client
ID plus that organisation's own FHIR endpoint, and potentially per-organisation enablement. How that
review process currently works for an individual, non-commercial app — and how long it takes — is
genuinely uncertain, and this design does not depend on it.

The pragmatic route is MyChart's own record export: a **C-CDA XML** document (typically under
Sharing → "Download My Record"), containing problems, medications, allergies, immunizations, lab
results, procedures and encounters as structured data. It needs no approvals at all. C-CDA R2.1 is an
HL7 standard with stable per-section template IDs, so a parser can be written against the
specification rather than reverse-engineered — but real documents vary in how completely they
populate it, so the importer is built defensively and gated behind a review step.

Direct Epic FHIR OAuth is explicitly **out of scope**, recorded here as a possible later spike.

## Scope for v1

Two phases, both delivered, sequenced so the manual model exists before anything imports into it.

**Phase 1 — manual medical history**
- Conditions, medications, allergies
- Visits, procedures, immunizations
- Lab results with trends over time
- Providers directory and document attachments
- Six pages behind the module's own sub-navigation

**Phase 2 — C-CDA import**
- Pick a MyChart C-CDA export, parse it, review what was found, then commit it to the database
- Idempotent: re-importing the same document does not duplicate rows
- Unreadable entries are skipped and counted, never silently dropped and never fatal

Explicitly out of scope for v1:
- Direct Epic/MyChart FHIR OAuth integration (see above)
- Writing anything back to MyChart — this is read/import only
- Clinical decision support of any kind: no interaction checking, no dosage validation, no
  interpretation of results. The module records what the user tells it and what their records say.
- Reminders, refill tracking, or appointment scheduling
- Encryption at rest (see "Storage and sensitivity")

## Storage and sensitivity

Medical data lives in the same shared `aaronos.db` as everything else, in plaintext, consistent with
`BodyMeasurements` and `Finance`. This was a deliberate choice: the file sits under `%LocalAppData%`
on a single-user machine protected by the Windows account, and field-level encryption would cost
queryability (an encrypted column cannot be searched, sorted or range-filtered in SQL) on precisely
the columns this module needs to search and sort. Revisit if the database ever leaves this machine.

`SchemaBootstrapper` (added to `AaronOS.Core` alongside the Nutrition work) creates tables for
newly-registered modules against an existing database, so this module's tables appear on next launch
without deleting the database or hand-writing SQL.

## Module shape

`AaronOS.Modules.Medical`, a class library following `docs/MODULE_GUIDELINES.md`:

```csharp
public class MedicalModule : IAppModule
{
    public string Id => "medical";
    public string DisplayName => "Medical";
    public string IconGlyph => "HeartPulse24"; // confirm exact SymbolRegular member at implementation time
    public Type HomePageType => typeof(MedicalShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<MedicalOverviewViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<MedicationsViewModel>();
        services.AddTransient<VisitsViewModel>();
        services.AddTransient<LabsViewModel>();
        services.AddTransient<ImportViewModel>();
    }
}
```

Appended last to the module array in `App.xaml.cs`, preserving Body Measurements as `modules[0]` and
therefore the landing page. `SettingsContentType` stays null — import is a multi-step workflow with a
review table, which belongs on its own page rather than squeezed into a Settings card.

Package references: `WPF-UI` 4.3.0 and `LiveChartsCore.SkiaSharpView.WPF` 2.0.5 (the lab trend
chart), matching the versions the other modules pin.

## Data model

Nine entities under `Data/`, each with an `IEntityTypeConfiguration<T>`, auto-discovered by
`AaronOsDbContext`. No module references another module's entities.

Every clinical record carries two provenance fields, which is what makes import safe and reversible
in the user's understanding:
- `Source` (`RecordSource` enum: `Manual`, `Imported`)
- `ExternalId` (`string?`) — the `<id>` from the source document, unique per record type where
  present, and the key used to recognise an already-imported record

Entities:
- **`MedicalCondition`** — `Id`, `Name`, `Code` (`string?`, ICD/SNOMED as given), `OnsetDate`
  (`DateOnly?`), `ResolvedDate` (`DateOnly?`), `Status` (`ConditionStatus`), `Notes`, `Source`,
  `ExternalId`
- **`Medication`** — `Id`, `Name`, `Dose`, `Frequency`, `StartDate` (`DateOnly?`), `EndDate`
  (`DateOnly?`), `ProviderId` (`int?` FK), `Notes`, `Source`, `ExternalId`
- **`Allergy`** — `Id`, `Substance`, `Reaction`, `Severity` (`AllergySeverity`), `Notes`, `Source`,
  `ExternalId`
- **`Immunization`** — `Id`, `Vaccine`, `DateGiven` (`DateOnly?`), `DoseNumber` (`int?`), `Notes`,
  `Source`, `ExternalId`
- **`MedicalProcedure`** — `Id`, `Name`, `Date` (`DateOnly?`), `ProviderId` (`int?` FK), `Facility`,
  `Notes`, `Source`, `ExternalId`
- **`MedicalVisit`** — `Id`, `Date` (`DateOnly?`), `VisitType`, `ProviderId` (`int?` FK), `Facility`,
  `Reason`, `Notes`, `Source`, `ExternalId`
- **`LabResult`** — `Id`, `TestName`, `Value` (`decimal?`), `ValueText` (`string?`), `Unit`,
  `ReferenceLow` (`decimal?`), `ReferenceHigh` (`decimal?`), `TakenOn` (`DateOnly?`), `Source`,
  `ExternalId`. Both a numeric and a text value because real results include "Negative" and
  "<0.01" alongside numbers, and forcing those into a decimal loses them.
- **`Provider`** — `Id`, `Name`, `Specialty`, `Phone`, `Facility`, `Notes`
- **`MedicalDocument`** — `Id`, `Title`, `FilePath`, `AddedOn` (`DateOnly`), `VisitId` (`int?` FK),
  `Notes`. Stores the path, never the bytes: attachments stay wherever the user keeps them, and the
  UI shows an explicit missing-file state rather than pretending a moved file is fine.

Enums: `ConditionStatus` (`Active`, `Chronic`, `Resolved`), `AllergySeverity` (`Unknown`, `Mild`,
`Moderate`, `Severe`), `RecordSource` (`Manual`, `Imported`).

Computed display members live on the entities, following the `FinanceTransaction.DateDisplay`
convention already established: `LabResult.IsOutOfRange`, `Medication.IsActive`,
`MedicalCondition.IsActive`, date and value display strings, and `MedicalDocument.FileExists`.

Units: lab units are stored as free text exactly as the source gives them (mg/dL, %, mmol/L). The
app's imperial-only convention covers body measurements, not clinical results, which are not ours to
normalise.

## Pages

`MedicalShellPage` (the module's `HomePageType`) — button row plus internal `Frame`, exactly like the
other three modules. No page nests its own `ScrollViewer` or `ListView`, per the note in
`FinanceDashboardPage.xaml`: the shell's `NavigationView` already provides the page scroller.

1. **`MedicalOverviewPage`** — allergies first, as a prominent banner rather than a row in a list,
   because they are the one item here whose whole purpose is to be seen before something goes wrong.
   Then active condition count, current medications, out-of-range labs, and the most recent visit.
2. **`HistoryPage`** — conditions, procedures and immunizations as three ledgers with inline add.
3. **`MedicationsPage`** — medications and allergies: what the user takes, and what they cannot.
4. **`VisitsPage`** — encounter log, provider directory, document attachments.
5. **`LabsPage`** — lab ledger plus a `LiveChartsCore` trend line for a selected test name, with
   reference-range shading where a range is known.
6. **`ImportPage`** — the C-CDA flow: choose file → parse → review → commit.

ViewModels derive from `AaronOS.Core.ViewModelBase`, resolve via
`AppServices.Provider.GetRequiredService<T>()` in each page's constructor, set `DataContext`
explicitly, and load from the `Loaded` event. `ui:NumberBox.Value` binds to `double?` with `null` as
"not entered" — never `double.NaN` (see the corrected note in `docs/MODULE_GUIDELINES.md`).

## C-CDA import

### Parsing

`CcdaParser` — a pure function over XML text, no DB and no file I/O, so it is fully unit-testable:

```csharp
public static CcdaDocument Parse(string xml)
```

Sections are located by C-CDA R2.1 `templateId/@root`, which is stable across conforming documents:

| Section       | templateId root                      |
|---------------|--------------------------------------|
| Problems      | `2.16.840.1.113883.10.20.22.2.5.1`   |
| Medications   | `2.16.840.1.113883.10.20.22.2.1.1`   |
| Allergies     | `2.16.840.1.113883.10.20.22.2.6.1`   |
| Immunizations | `2.16.840.1.113883.10.20.22.2.2.1`   |
| Results       | `2.16.840.1.113883.10.20.22.2.3.1`   |
| Procedures    | `2.16.840.1.113883.10.20.22.2.7.1`   |
| Encounters    | `2.16.840.1.113883.10.20.22.2.22.1`  |
| Vital signs   | `2.16.840.1.113883.10.20.22.2.4.1`   |

Sections are matched on template ID with a fallback to the section `code/@code` (LOINC), because some
producers omit or version the template ID. A section that cannot be found is simply absent from the
result — not an error.

Handled explicitly, because real documents do all of it:
- HL7 `TS` timestamps: `YYYYMMDD`, `YYYYMMDDHHMMSS`, and either with a `±ZZZZ` offset
- `nullFlavor` attributes standing in for missing values
- `<value xsi:type="PQ" value="5.7" unit="%"/>` for numeric results, `ST`/`CD` for textual ones
- `referenceRange/observationRange/value` low and high
- Display text carried in a `<reference value="#id"/>` pointing into the section's narrative table,
  rather than in the coded element — resolved by looking the id up in the narrative
- Entries wrapped in `entryRelationship`, `substanceAdministration`, and `act/entryRelationship`
  nesting for medications and allergies

Every entry the parser cannot make sense of increments a per-section skip count and appends a warning
string. `CcdaDocument` therefore carries both what was read and an honest account of what was not.

**Vital signs are parsed but body weight is deliberately excluded** from what gets imported. Body
weight belongs to `BodyMeasurements`, modules may not write to each other's tables, and two sources
of truth for the same number is worse than one. Blood pressure, heart rate and similar clinical
observations import as `LabResult` rows. This is a judgment call, called out at the point it is
applied in code.

### Review and commit

`ImportPlanner` — a second pure function that classifies parsed records against what is already in
the database:

```csharp
public static ImportPlan BuildPlan(CcdaDocument parsed, ExistingKeys existing)
```

Each candidate becomes `New`, `AlreadyImported` (matching `ExternalId` for its type), or `Skipped`
(unreadable). `ExistingKeys` is a plain snapshot of the `ExternalId` sets per record type, passed in
by the ViewModel, which keeps the planner free of EF.

`ImportPage` shows counts per section and a per-record table with its classification, and only
writes on explicit confirmation. Records without an `ExternalId` fall back to a natural key —
`(TestName, TakenOn, Value)` for labs, `(Name, OnsetDate)` for conditions, and so on — so a producer
that omits ids still does not duplicate on re-import.

Everything written by an import is stamped `Source = Imported`, so the UI can distinguish it from
hand-entered data and the user can always tell where a row came from.

## Error handling

- Malformed or non-C-CDA XML: caught, reported on the Import page as a single clear message, nothing
  written. A file that is not a C-CDA at all is a user mistake, not a crash.
- Partially readable document: imports what parsed, reports per-section skip counts and warnings.
- Missing attachment file: `MedicalDocument.FileExists` is false and the row renders in the
  expired/missing colour, rather than failing to open silently.
- Duplicate manual entry: no unique constraints on clinical names — the same condition can legitimately
  be recorded twice with different dates, so this is not treated as an error.

## Testing

`AaronOS.Modules.Medical.Tests` (xunit, `net8.0-windows`, no `UseWPF`), covering the logic that is
genuinely worth a guard — the same standard the other modules' test projects hold:

1. HL7 timestamp parsing: date-only, full timestamp, with offset, `nullFlavor`, and garbage
2. `CcdaParser` per section against small fixture documents authored from the R2.1 spec: problems,
   medications, allergies, immunizations, results, procedures, encounters
3. `CcdaParser` narrative-reference resolution (`<reference value="#id"/>` → narrative text)
4. `CcdaParser` skip counting and warnings on malformed entries
5. Body-weight exclusion from vital signs
6. `ImportPlanner` classification: new, already-imported by `ExternalId`, de-duplicated by natural
   key when `ExternalId` is absent
7. `LabResult.IsOutOfRange` including the one-sided-range and no-range cases
8. `Medication.IsActive` / `MedicalCondition.IsActive` date logic

The fixture documents are the important part: they are what let the parser be developed and refined
without the user's real export, and they are what will catch a regression when it is refined against
that export later.
