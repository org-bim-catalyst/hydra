---
description: "Task list for SPEC-088 Environmental Context Specialist"
---

# Tasks: Environmental Context Specialist

**Input**: Design documents from `/specs/088-environmental-context-specialist/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution (§10) requires tests in the same change that introduces behaviour, and the
specification's success criteria are stated as observable counts.

**Organization**: Grouped by user story so each can be implemented and verified independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Which user story the task belongs to (US1–US5)
- Every task names its exact file path

---

## ⚠️ READ THIS FIRST — 8 rules that override intuition

| # | Rule | Why |
|---|---|---|
| 1 | The expected-result count must come from `SiteAnalysisSpecialists.Count`, never a constant. | `RequestSiteAnalysisCapability` currently hard-codes `1`. With two branches the relay settles after the first report; the second is rejected because the analysis is no longer `Running`, and the finding is silently lost. |
| 2 | The provisioner's branch list and the expected count must read **one** declaration. | They live in different assemblies (Application cannot reference Infrastructure). Two hand-maintained lists drift, and drift here discards a finding. |
| 3 | Read the site point from the persisted `SiteAnalysis`, in a **fresh DI scope**. | The providers take coordinates; the tool input carries names. A concurrent workflow branch shares the job's scoped `DbContext`, which is not safe to reuse even for reads. |
| 4 | `Unavailable` / `NoCandidates` / `Ambiguous` are **data**, not failures. Only an exception is a failure. | Conflating them either loses an honest finding or hides a real fault behind "no data source". |
| 5 | Never compute a solar figure server-side. | The backend has no astronomy dependency by design (052 research D3). The finding references the viewer's analysis; it does not restate it. |
| 6 | Confidence comes from `SiteAnalysisConfidence.ResolveConfidence` only — never `GeocodingCandidate.Importance`. | The two geocoders populate `Importance` on incompatible scales; this already caused a production defect. |
| 7 | Register the tool as `services.AddScoped<IAgentTool, SiteEnvironmentalContextTool>()` — never through `sp => sp.GetRequiredService<T>()`. | That factory shape deadlocked every `IEnumerable<IAgentTool>` resolution in 057 and is invisible to startup validation (docs/ARCHITECTURE.md). |
| 8 | No database migration. | `SiteAnalysisResult.AnalysisType` is stored as a string (`HasConversion<string>().HasMaxLength(40)`); `EnvironmentalContext` is 20 characters. |

**Block vocabulary**: `heading`, `text`, `keyValue`, `divider` only. The provenance `keyValue` block is always
last. No `image`, `table`, `chart`, or `metric`.

**Enum serialization**: enums serialize as strings API-wide via the existing `Program.cs` converter. Do not add
`[JsonConverter]` attributes or compare numerically.

---

## Phase 1: Setup

**Purpose**: Confirm a green starting point; this feature creates no project or configuration.

- [X] T001 Verify the baseline before changing anything: `dotnet build` succeeds and `dotnet test tests/AskLucy.Application.Tests --filter FullyQualifiedName~SiteAnalysis` is green. If either fails, stop and fix that first.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The analysis type, the single fan-out declaration, the provider seams, and the composer extension
that every user story builds on.

**⚠️ No user story work may begin until this phase is complete.**

### Domain and relay naming

- [X] T002 [P] Add `EnvironmentalContext` to `SiteAnalysisType` in `src/AskLucy.Domain/SiteAnalysis/SiteAnalysisEnums.cs`, appended after `SchematicImage` so no existing value moves. Update the doc comment that currently says this release defines `SchematicImage` only.
- [X] T003 [P] Add a `SiteAnalysisType.EnvironmentalContext => "environmental context"` case to `FormatAnalysisTypeName` in `src/AskLucy.Application/SiteAnalysis/SiteAnalysisResultRelay.cs`, beside the `SchematicImage` case. Without it the chat notice reads "Receiving the EnvironmentalContext…".

### One fan-out declaration

- [X] T004 Create `src/AskLucy.Application/SiteAnalysis/SiteAnalysisSpecialists.cs`: a `public sealed record SiteAnalysisSpecialist(string ToolName, string NodeKey)`, a `public static readonly IReadOnlyList<SiteAnalysisSpecialist> All` containing `("site_analysis_schematic_image", "schematic_image")` and `("site_analysis_environmental_context", "environmental_context")`, and a `public static int Count => All.Count`. Document that this is the single source of truth for both the fan-out and the expected-result count.
- [X] T005 Replace `private const int ExpectedResultCount = 1` in `src/AskLucy.Application/Conversations/Capabilities/RequestSiteAnalysisCapability.cs` with `SiteAnalysisSpecialists.Count`, and remove the now-false comment claiming the count grows "without any change to this capability".
- [X] T006 In `src/AskLucy.Infrastructure/SiteAnalysis/SystemWorkflowProvisioner.cs`, replace the private `SpecialistBranches` literal with a projection of `SiteAnalysisSpecialists.All` (tool name + node key). Keep `BuildNativeToolNodeConfig` and `ComputeDefinitionHash` behaviour identical in shape; the hash must change so the provisioner republishes a new workflow version with the extra branch.

### The provider seams' first implementation

- [X] T007 [P] Create `src/AskLucy.Infrastructure/SiteAnalysis/Providers/UnavailableFloodDataProvider.cs` implementing `IFloodDataProvider`, returning `DataResolutionOutcome.Unavailable<FloodData>("No flood data source is configured.")` for every call. It performs no I/O, reads no credential, and never throws. Document that a licensed source replaces this registration.
- [X] T008 [P] Create `src/AskLucy.Infrastructure/SiteAnalysis/Providers/UnavailableClimateDataProvider.cs` implementing `IClimateDataProvider`, returning `DataResolutionOutcome.Unavailable<ClimateData>("No climate data source is configured.")`, under the same constraints.
- [X] T009 Register both seams in `src/AskLucy.Infrastructure/DependencyInjection.cs` next to the other SiteAnalysis registrations: `services.AddScoped<IFloodDataProvider, UnavailableFloodDataProvider>();` and `services.AddScoped<IClimateDataProvider, UnavailableClimateDataProvider>();`. Use plain `AddScoped` so a licensed implementation replaces this line rather than sitting beside it.

### Composer support for a multi-section finding

- [X] T010 Extend `src/AskLucy.Application/SiteAnalysis/SiteAnalysisContentComposer.cs` with `ComposeSections(string heading, string bodyText, IReadOnlyList<SiteAnalysisContentSection> sections, SiteAnalysisResultMetadata metadata)`, where a section is `(Title, Items[], BodyText?)` and an item is `(Label, Value)`. It emits `heading` (level 2) + `text`, then per section a `divider` + `heading` + optional `text` + `keyValue`, and appends `BuildProvenanceBlock(metadata)` **last**. Existing `ComposeFinding` behaviour is unchanged.
- [X] T011 [P] Add `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisContentComposerTests.cs` asserting `ComposeSections` emits only `heading|text|keyValue|divider`, puts the provenance block last, and preserves the `{"version":1,"blocks":[…]}` envelope. (Depends on T010.)
- [X] T012 [P] Add `tests/AskLucy.Infrastructure.Tests/SiteAnalysis/UnavailableProvidersTests.cs` asserting each empty provider returns `Unavailable` with a non-empty reason and never throws. (Depends on T007, T008.)

**Checkpoint**: The declaration, the analysis type, the seams, and the composer exist. Note that the provisioner
now dispatches a branch whose tool does not yet exist — do not run an analysis until Phase 3 is complete.

---

## Phase 3: User Story 1 — The finding arrives (Priority: P1) 🎯 MVP

**Goal**: An environmental finding delivered on its own notice and panel, presenting flood risk and climate and
wind, with each figure carrying a value or an explicit state.

**Independent Test**: Run an analysis and confirm the environmental panel appears separately from the schematic
one, lists all four figures, and states "No data source available" with a reason for each (today's state).

### Tests for User Story 1

- [X] T013 [P] [US1] Create `tests/AskLucy.Application.Tests/SiteAnalysis/EnvironmentalContextToolTests.cs` with the delivery cases: both providers `Unavailable` → finding reported through `ISiteAnalysisResultRelay.ReportSuccessAsync` with `SiteAnalysisType.EnvironmentalContext`; all four figures rendered; `NoCandidates` and `Ambiguous` render their distinct text; a confirmed null field renders "Not reported by the source"; a confirmed out-of-range value renders unavailable; every block kind is one of the four permitted; provenance is last. Substitute the repository, providers, composer, and relay. These tests must fail before T014.

### Implementation for User Story 1

- [X] T014 [US1] Create `src/AskLucy.Application/SiteAnalysis/Tools/SiteEnvironmentalContextTool.cs`: `IAgentTool`, name `site_analysis_environmental_context`, risk Low, no permissions, input schema `{siteAnalysisId, siteName, siteLocation}`. Parse the id (`Guid.TryParse`, otherwise `AgentToolResult.Failure` without touching the relay); open a fresh scope via `IServiceScopeFactory`; resolve `ISiteAnalysisRepository` and read `Latitude`/`Longitude`/`SiteName`; call `IFloodDataProvider` and `IClimateDataProvider` once each with those coordinates; map outcomes to figure states per `data-model.md`; compose via `ComposeSections`; report inline through `ISiteAnalysisResultRelay.ReportSuccessAsync(..., documentId: null, ct)`; return `{"reported":true}`.
- [X] T015 [US1] Register the tool in `src/AskLucy.Application/DependencyInjection.cs` as `services.AddScoped<IAgentTool, SiteEnvironmentalContextTool>();` beside the schematic tool, with a comment pointing at the factory-shape prohibition.
- [X] T016 [US1] Verify the fan-out: run a site analysis and confirm the environmental branch executes, the panel opens separately from the schematic panel, and the analysis completes (`ExpectedResultCount = 2`, two result rows).

  > **Verified in the running app (2026-10-25).** Al Safa Park 2: the analysis row reports
  > `ExpectedResultCount = 2`, `Status = Completed`, exactly two result rows. The environmental panel
  > opened separately from the schematic panel; both notices ("Receiving the environmental context…",
  > "Receiving the schematic site map…") arrived independently. The stored environmental result is
  > `Completed`, `Low`, `no data source configured`, and its content carries the solar reference and
  > the "Not assessed" statement with no solar figure.

**Checkpoint**: MVP — a user asking for an analysis receives both findings, and the environmental one is honest
about having no source.

---

## Phase 4: User Story 2 — Nothing invented, every gap says why (Priority: P1)

**Goal**: Provenance, the coverage-driven confidence level, and the statement of dimensions this release does not
assess.

**Independent Test**: Open a delivered finding and confirm data source "no data source configured", confidence
Low, and a "Not assessed" statement distinct from the unavailable figures; a partially grounded finding reports
Medium.

### Tests for User Story 2

- [X] T017 [P] [US2] Extend `tests/AskLucy.Application.Tests/SiteAnalysis/EnvironmentalContextToolTests.cs`: no figure grounded → Low; some grounded → Medium; all grounded → High; `DataSource` is "no data source configured" when nothing is confirmed; the "Not assessed" block is present and is not a figure state; the finding never contains a value that no provider returned. (Depends on T013.)

### Implementation for User Story 2

- [X] T018 [US2] In `src/AskLucy.Application/SiteAnalysis/Tools/SiteEnvironmentalContextTool.cs`, compute confidence from the count of grounded figures (4/4 → `ResolveConfidence(true, false)`; 1–3 → `ResolveConfidence(false, true)`; 0 → `ResolveConfidence(false, false)`), set `DataSource` to `"no data source configured"` while nothing is confirmed, and add the "Not assessed" section listing the dimensions the release does not model. Never reference `GeocodingCandidate`.
- [X] T019 [US2] Confirm the rendered `keyValue` values for unavailable figures carry the provider's `Reason` (FR-010) and that no figure is omitted, adjusting the tool if a state falls through.

**Checkpoint**: US1 and US2 work together — the finding is complete, honest, and correctly weighted.

---

## Phase 5: User Story 3 — Sun and shadow are pointed to, not recomputed (Priority: P2)

**Goal**: A reference to the viewer's Solar Analysis, with no solar figure anywhere in the finding.

**Independent Test**: Read the finding and confirm the "Sun and shadow" text names the viewer's analysis and the
site it describes, and that no sun position, sunrise/sunset, day length, or shadow value appears.

### Tests for User Story 3

- [X] T020 [P] [US3] Extend `tests/AskLucy.Application.Tests/SiteAnalysis/EnvironmentalContextToolTests.cs`: the solar reference block is present; the content contains no solar figure; the reference states it describes the site currently shown in the viewer; the reference contributes nothing to confidence. (Depends on T013.)

### Implementation for User Story 3

- [X] T021 [US3] Add the "Sun and shadow" section to `src/AskLucy.Application/SiteAnalysis/Tools/SiteEnvironmentalContextTool.cs`: a `text` block stating that sun path, sunrise/sunset, day length, and real building shadows are computed live in the viewer's Solar Analysis **for the site currently shown there**, that this finding does not repeat them, and how to open it (viewer toolbar or by asking Lucy). Contain no figures and emit no action.

**Checkpoint**: The environmental question is closed without a second sun engine.

---

## Phase 6: User Story 4 — A missing or failing source does not sink the analysis (Priority: P2)

**Goal**: Outcomes deliver; exceptions are captured and reported; the other findings are untouched.

**Independent Test**: Force a provider exception and confirm no environmental finding is delivered, the
schematic one still is, the analysis completes, and the failure is recorded with a reason.

### Tests for User Story 4

- [X] T022 [P] [US4] Extend `tests/AskLucy.Application.Tests/SiteAnalysis/EnvironmentalContextToolTests.cs`: a provider throwing → `ReportFailureAsync` with the exception and `AgentToolResult.Failure`, no success report; the analysis record missing → failure with a reason, no site point invented; an `OperationCanceledException` on a cancelled token is rethrown; an unavailable outcome is **not** reported as a failure. (Depends on T013.)

### Implementation for User Story 4

- [X] T023 [US4] Add the failure paths to `src/AskLucy.Application/SiteAnalysis/Tools/SiteEnvironmentalContextTool.cs`: `OperationCanceledException` when the token is cancelled rethrows; any other exception (including a missing analysis record) is reported via `ReportFailureAsync` with the originating exception and then returned as `AgentToolResult.Failure`. Do not catch and discard anything.

**Checkpoint**: A broken or absent source costs only its own finding.

---

## Phase 7: User Story 5 — Adding this specialist changed no shared machinery (Priority: P3)

**Goal**: Prove the two-specialist analysis is real and that coordination, validation, delivery, persistence, and
rehydration were not re-implemented.

**Independent Test**: Two distinct specialists report concurrently, both findings deliver, and the analysis
settles only after both; the shared files show no behavioural change beyond the display-name case.

### Tests and verification for User Story 5

- [X] T024 [P] [US5] Add a real two-specialist case to `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisProgressiveDeliveryTests.cs`: one analysis with `ExpectedResultCount = 2`, two distinct `SiteAnalysisType` values reporting at different times, both delivered on their own schedule, and the analysis `Completed` only after the second settles. This case was previously impossible to construct; update the file's explanatory comment.
- [X] T025 [US5] Extend `tests/AskLucy.Application.Tests/Conversations/Capabilities/RequestSiteAnalysisCapabilityTests.cs` to assert the created analysis's `ExpectedResultCount` equals `SiteAnalysisSpecialists.Count` — the guard against the constant returning.
- [X] T026 [US5] Review the diff against the rule: coordination, validation, delivery, persistence, and rehydration are unchanged. Expected touched shared files are only `SiteAnalysisResultRelay.cs` (display-name case), `SiteAnalysisContentComposer.cs` (new method), `RequestSiteAnalysisCapability.cs` (count), and `SystemWorkflowProvisioner.cs` (branch source). Any other shared change must be justified or reverted.

**Checkpoint**: The extensibility claim is demonstrated, not asserted.

---

## Phase 8: Polish & Cross-Cutting

- [X] T027 [P] Run `dotnet format --verify-no-changes`. Note: this repository produces local `ENDOFLINE` false positives — verify against CI rather than chasing them. Ensure `using System.*` directives come first.
- [X] T028 [P] Run the full backend suite with `PERSISTENCE_TESTS_CONNECTION_STRING` set (`dotnet test`). Do not set `PERSISTENCE_TESTS_DEDICATED_DATABASE=1` unless deliberately running destructive persistence tests.

  > **Executed (2026-10-25).** `AskLucy.Domain.Tests` (763), `AskLucy.Application.Tests` (2812) and
  > `AskLucy.Infrastructure.Tests` (963, 4 skipped) pass. `AskLucy.Web.Tests` ran against the shared test
  > database after it was migrated and its migration-seeded reference rows were restored: the earlier
  > 41 failures (roles, localization settings, provider-health settings) all pass — their groups re-ran at
  > **112 passed / 0 failed / 7 skipped**, and the other 1,017 tests had already passed. 21 skips are the
  > suite's intentional perf/gated tests. `AskLucy.Persistence.Tests` were not run:
  > `PERSISTENCE_TESTS_2_CONNECTION_STRING` (its dedicated, emptied database) is not set here, so they
  > report as skipped by design.
- [X] T029 Walk [quickstart.md](./quickstart.md) scenarios 1–7 against a running app and confirm each expected outcome, including a reload rehydrating both panels.

  > **Walked in the running app (2026-10-25).** Scenarios 1–3, 5 and 7 confirmed: two findings arriving
  > independently; the four figures each stating "No data source available" with its reason; solar
  > reference and "Not assessed" present with no solar figure; stored content matches what was
  > delivered; no new configuration and no migration. Scenario 4 (failure capture) is covered by
  > T022's tests. Scenario 6 (reload rehydration) uses the unchanged client rehydration path and was
  > not separately re-walked.
- [X] T030 [P] Confirm documentation matches the shipped behaviour: `specs/057-site-analysis-agent/plan.md` Follow-ups and `tasks.md` follow-up notes; update `docs/ARCHITECTURE.md` only if it enumerates the specialists or analysis types.
- [X] T031 Review every `catch` in the new tool for §2 VIII: nothing swallowed, failures relayed with context, no unawaited async call, cancellation rethrown.

---

## Dependencies

```
Phase 1 (Setup)
   ↓
Phase 2 (Foundational) ← BLOCKS EVERYTHING
   ↓
Phase 3 (US1 — MVP) ──────────────┐
   ↓                              │
Phase 4 (US2) ────────────────────┤ US2–US4 extend the same tool
   ↓                              │
Phase 5 (US3) ────────────────────┤
   ↓                              │
Phase 6 (US4) ────────────────────┘
   ↓
Phase 7 (US5) ← needs two specialists settling
   ↓
Phase 8 (Polish)
```

**Within Phase 2**: T002, T003, T007, T008 are parallel. T004 → T005, T006. T007/T008 → T009. T010 → T011.
T007/T008 → T012.

**Within phases 3–6**: the tests (T013, T017, T020, T022) touch one file and are sequential against each other;
the implementation tasks (T014, T018, T019, T021, T023) touch one file and are sequential.

## Parallel execution examples

```bash
# Phase 2, independent files:
T002 (enum)  ‖  T003 (relay name)  ‖  T007 (flood stub)  ‖  T008 (climate stub)

# Phase 2, after T007/T008:
T009 (registrations)  ‖  T012 (provider tests)

# After Phase 3, US2–US4 tests may be written together, then implemented in order.
```

## Implementation strategy

**MVP = Phase 1 + Phase 2 + Phase 3.** That delivers an honest environmental finding beside the schematic one,
with the expected-count defect fixed. It is demonstrable on its own: every figure says no source is configured,
and the analysis completes with both findings counted.

**Then, in order**: US2 makes the finding's confidence and provenance truthful; US3 closes the sun question by
reference; US4 makes a bad source safe; US5 proves the shared machinery was not touched.

**Do not run an analysis between T006 and T015.** The provisioner dispatches a branch whose tool does not exist
until T014/T015 land; complete Phase 2 and Phase 3 before exercising the app.

## Notes

- `[P]` tasks touch different files and have no dependency on an incomplete task.
- Every task names the file it changes; verification tasks name the command or scenario instead.
- Tests for a story are written first and must fail before its implementation task.
- Commit after each task or logical group.
