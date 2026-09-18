# Quickstart: Site Analysis Agent (SPEC-057)

**Date**: 2026-09-17 | **Plan**: [plan.md](./plan.md) | **Spec**: [spec.md](./spec.md)

How to run and validate this feature end to end. Each scenario names the requirements and success criteria it
proves, so a reviewer can confirm coverage without re-reading the spec.

---

## Prerequisites

**Image generation is configured in the app, not in a config file.** The schematic map (and the chat's own
image generation) runs on whatever the administrator assigns to the **Image generation** capability:

1. **Admin → AI Models**: make sure an image model is in the catalogue, **Available**, and has the
   *image output* capability ticked (e.g. OpenAI `gpt-image-2`, or Google `gemini-3-pro-image-preview`).
   Add it from the provider's model sync if it is missing.
2. **Admin → AI Capabilities → Image generation**: pick the provider, then the image model. Nothing is saved
   until a model is chosen — a provider's default model is a chat model and cannot draw.

Until that is done, image generation reports "not configured" rather than silently trying a chat model. OpenAI
and Google Gemini implement image generation; Anthropic and OpenRouter throw `NotSupportedException`. Any
response form a provider returns (hosted URL, base64, data URL, binary) is handled.

> **Verified live (2026-09-18):** production's Anthropic and Google Gemini credentials were rotated
> 2026-09-17 and have passed every automated health check since (2-minute interval, continuous) —
> both are genuinely reachable, not just configured. Anthropic still has no image-capable model in
> its lineup (`GenerateImageAsync` correctly throws `NotSupportedException`; this is a real vendor
> gap, not a gap in this feature). Production's existing `gemini-3-pro-image-preview` catalog row
> predates this feature and was added before `SupportsImageOutput` inference existed for Gemini, so
> it is **not** flagged as an image model yet — model sync never re-touches a model already in the
> catalog (`GetProviderModelSyncDiffQueryHandler`: only new/missing vendor models are proposed).
> Only this feature's `AddCapabilityAssignmentModel` migration backfills it (by name pattern); until
> that migration ships to production, an operator there would need to remove and re-add the model
> via sync, or wait for the migration.

> **Superseded (2026-09-18):** earlier drafts of this page told you to add a `SiteAnalysis` section
> (`ImageGenerationProviderKey` / `ImageGenerationModel`) to `appsettings.Development.json`. That section is no
> longer read — delete it. `Ai:OpenAI:ImageModel` is gone for the same reason.

**Database** — apply the new migration before first run:

```bash
dotnet ef database update --project src/AskLucy.Persistence --startup-project src/AskLucy.Web
```

**A resolvable site** — the analysis runs against a confirmed site, so have one in mind that the existing
boundary pipeline resolves cleanly (e.g. "Al Barsha South").

## Run

```bash
# Backend + SPA
dotnet run --project src/AskLucy.Web          # https://localhost:7170

# SPA alone, against a running backend
cd src/AskLucy.Web/ClientApp && npm run dev
```

---

## Scenario 1 — Dispatch returns immediately

**Proves**: FR-003, SC-001 · **Story 1**

1. Open a conversation and bring a site into view ("show me Al Barsha South"). Confirm the boundary resolves as
   it does today — this is the reuse point (FR-002), not new behavior.
2. Ask: *"Run a site analysis on this site."*

**Expected**: Lucy acknowledges within ~3 s, naming the site and saying analysis has begun. The turn **ends** —
it does not sit streaming while the workflow runs.

**Fails if** the turn hangs until the image is ready — that means the capability awaited the workflow instead of
enqueuing it.

Confirm the workflow was provisioned at startup:

```sql
SELECT Id, Name, SystemKey, IsSystemOwned, Status, PublishedVersionNumber
FROM Workflows WHERE SystemKey = 'site-analysis';
```

---

## Scenario 2 — A finding arrives through the parent, in the right order

**Proves**: FR-005, FR-007, FR-008, FR-009, SC-002 · **Story 1**

Watch the conversation and the viewer after Scenario 1.

**Expected, in this order**:

1. An assistant notice: *"Receiving the schematic site map for …"*
2. A floating panel opens over the viewer containing the map.

**Verify the delivery path** — open DevTools, Network, WS:

- One `SiteAnalysisResultReceived` frame on `/hubs/site-analysis`
- One `PanelRequested` frame on `/hubs/panels`, correlated by `resultId`
- Both arrive when the specialist finishes — **not** when the workflow completes

**Fails if** both frames arrive only at workflow completion: the implementation is riding the workflow's node
notifications, which `ExecuteParallelAsync` batches until every branch settles (research D3). The specialist
must call the relay inline.

---

## Scenario 3 — The image is a platform document, not a provider URL

**Proves**: FR-029, constitution §8 · **Story 2**

In DevTools, Elements, inspect the panel's image element.

**Expected**: `src` resolves through `/documents/{id}/download` with a signed URL.

**Fails if** `src` points at an OpenAI or blob-storage host — the bytes were not persisted through
`DocumentUploadFinalizer`. The block schema should reject this outright, so seeing it means validation was
bypassed.

```sql
SELECT r.AnalysisType, r.Status, r.DataSource, r.ConfidenceLevel, r.DocumentId
FROM SiteAnalysisResults r
JOIN SiteAnalyses a ON a.Id = r.SiteAnalysisId
ORDER BY a.StartedAtUtc DESC;
```

`DocumentId` must be populated and must resolve to a `Documents` row owned by the requesting user.

---

## Scenario 4 — Provenance is visible

**Proves**: FR-011, FR-013, SC-003 · **Story 2**

Read the delivered panel.

**Expected**: heading, the map, and a key/value block showing analysis type, data source
(`openai:<model>`), confidence (`Medium`), and generation time.

`Medium` is correct and deliberate — the site is exact, the rendering is generative (research D10).

---

## Scenario 5 — Findings survive navigation and reload

**Proves**: FR-016 through FR-019, SC-004 · **Story 3**

1. Start an analysis.
2. Navigate away before it finishes (another route, or a full reload).
3. Return to the conversation after it completes.

**Expected**: the completed finding is present, with identical content and provenance.

**Verify**: `GET /api/v1/site-analyses/{id}` returns the analysis with its results, and the client fetched it on
conversation open rather than relying on the hub.

**Ownership** — sign in as a different user and request the same id.

**Expected**: `404` (not `403`), as Problem Details. Existence is not disclosed (FR-019, SC-009).

---

## Scenario 6 — Failure is silent to the user, loud to the operator

**Proves**: FR-020 through FR-024, FR-030, SC-005, SC-006, SC-011 · **Story 4**

Break image generation deliberately — clear the **Image generation** capability assignment (it then reports
"not configured"), or invalidate the assigned provider's credential. Run an analysis.

**Expected in the UI**: no failure notice, no failure panel, no error toast for the specialist itself.

**Expected in the record**:

```sql
SELECT Status, FailureReason FROM SiteAnalysisResults
WHERE SiteAnalysisId = '<id>';
```

`Status = 'Failed'` with a `FailureReason` carrying the classified provider error.

**Expected in the logs**: one structured entry with analysis id, site name, analysis type, user id, and the
exception. This is what satisfies constitution §2 VIII — capture and diagnosability, not user disclosure.

**Fails if** the catch block discards the exception, or logs a bare message with no context.

---

## Scenario 7 — The analysis always ends

**Proves**: FR-021, FR-025 through FR-027, SC-010 · **Story 4**

Continue from Scenario 6 (the only specialist failed).

**Expected**: exactly one closing message — *"I couldn't complete the site analysis."* — and
`SiteAnalyses.Status = 'Failed'` with `CompletedAtUtc` set.

The user must never be left holding only the start acknowledgement.

**Then restore the configuration** and confirm the all-succeeded case emits **no** closing message
(`noticeText` is null) — the finding is its own report.

---

## Scenario 8 — Progressive arrival, with a second specialist

**Proves**: FR-004, FR-005, FR-031, SC-002, SC-008 · **Story 1 scenario 3, Story 5**

Only one specialist ships in this release, so progressive arrival is not observable from the product alone.
Verify it by temporarily adding a throwaway specialist:

1. Add a `SiteAnalysisType` member and a trivial `IAgentTool` that sleeps about 10 s, then reports a one-block
   finding through the relay.
2. Register it and add one branch entry to the system workflow provisioner.
3. Run an analysis.

**Expected**: two findings arrive at **different** timestamps, each when its own specialist finished — the fast
one does not wait for the slow one. The analysis completes after both settle.

**Also proves FR-031**: this required no change to the relay, dispatcher, persistence, notifier, or client.

**Remove the throwaway specialist afterwards.**

---

## Scenario 9 — One failure does not sink the rest

**Proves**: FR-020, SC-005 · **Story 4**

With the throwaway specialist from Scenario 8 still present, break image generation again.

**Expected**: the throwaway finding still arrives; the analysis reaches `Completed` (not `Failed`) because at
least one specialist succeeded; one closing message reports partial completion.

**Fails if** the whole analysis fails — the Merge node is not using the tolerant `AnyCompleted` strategy
(research D13).

---

## Automated checks

```bash
# Backend
dotnet build
dotnet test tests/AskLucy.Domain.Tests
dotnet test tests/AskLucy.Application.Tests

# Suites touching persistence need a connection string; without it almost
# everything fails at Hangfire startup, which looks unrelated but is not.
export PERSISTENCE_TESTS_CONNECTION_STRING="<value from appsettings.Development.json>"
dotnet test tests/AskLucy.Web.Tests

# Persistence tests are destructive against the dev database unless opted in.
export PERSISTENCE_TESTS_DEDICATED_DATABASE=1
dotnet test tests/AskLucy.Persistence.Tests

# Frontend — `tsc --noEmit` alone is a silent no-op here (project references).
cd src/AskLucy.Web/ClientApp
npx tsc -b --noEmit
npm run lint
npm test
```

Run the **full** frontend suite, not just the touched files — page-level tests carry their own assertions
independent of a component's own test file.

---

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| All findings arrive together at the end | Delivery is riding batched workflow node events instead of the inline relay (research D3) |
| Nothing arrives, no error | `RunByUserId` was set to a system id — both notifiers key by user id, so the push went nowhere (research D12) |
| Workflow never starts; ownership error | Dispatch went through `StartWorkflowExecutionCommand`, whose guard requires `OwnerId == userId`. Use the dispatcher (research D12) |
| Startup crash on a fresh database | Provisioner is missing the defer-on-pending-migrations behavior it must mirror from `SystemAgentProvisioner` |
| Image panel shows "unavailable" | `fileId` is not a valid platform document id, or the document is not owned by the requesting user |
| Document creation rejects the PNG | `IDocumentFileValidator`'s accepted-type set may not include PNG — the open verification item in `contracts/schematic-image-prompt.md` |
| Enum comparisons fail on the client | Enums must serialize as strings API-wide; a numeric enum here breaks every string comparison |
| Panels vanish after navigating | Rehydration fetch is missing — the hub is not a persistence mechanism (see `contracts/site-analysis-api.md`) |
