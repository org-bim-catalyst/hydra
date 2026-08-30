# Implementation Plan: Location & Site-Boundary Regression Fix

**Branch**: `044-location-viewer-regression` | **Date**: 2026-08-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/044-location-viewer-regression/spec.md`

## Summary

Site-boundary resolution was inserted between resolving a location and delivering it to the viewer, with neither a failure boundary nor a time boundary. An optional enhancement became a hard prerequisite for a mandatory outcome.

The fix restores the pre-`88b631a` property — **zero network calls between resolving a location and delivering it** — in three moves:

1. **Reorder** — the handler yields the confirmed-location chunk *before* awaiting the boundary, and the controller flushes `__LOCATION__` the moment it sees that chunk instead of after the stream drains.
2. **Isolate** — wrap the boundary step so no exception of any type can escape into the chat turn, honouring the contract `BoundaryResolutionService` already documents but does not implement.
3. **Bound** — cap the whole boundary step at 45 seconds, since per-dependency timeouts still sum to ~90s.

Plus one correctness repair the clarification surfaced: clear the stored boundary whenever a confirmed location names a different site, so stored state and prompt context can never outlive the site they describe.

Gemini Vision keeps its geometry-correction capability and plausibility check untouched.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript 5 / React 19 (client unchanged by this feature)

**Primary Dependencies**: MediatR (streaming `CreateStream`), ASP.NET Core SSE, `IHttpClientFactory`; Overpass, ESRI World Imagery, and Google Gemini as external boundary/vision dependencies

**Storage**: SQL Server via EF Core — `UserChats.ActiveBoundary` owned entity. **No schema change required**; the fix clears an existing column set, it does not add one.

**Testing**: xUnit v3 + NSubstitute + FluentAssertions (`tests/AskLucy.Application.Tests`); Vitest for the client (no client change expected)

**Target Platform**: ASP.NET Core web service on a shared host (site4now.net), which has twice produced false failures from short client-side timeouts

**Project Type**: Web application — Clean Architecture backend (Domain / Application / Infrastructure / Web) plus a React client

**Performance Goals**: Viewer receives the confirmed location within 5 s of the model's text ending (SC-002); the turn completes within 45 s even with every boundary/vision dependency hanging (SC-007)

**Constraints**: Boundary resolution stays inside the chat turn (no new delivery channel); Gemini Vision must remain strictly additive; no unrelated work modified

**Scale/Scope**: 3 backend files changed, ~4 test files added/extended. Deliberately small — this is a regression repair, not a redesign.

### Context correction since the spec was written

`8e83b8f` (spec-043, *classify provider failures*) landed on `main` between the clarification session and this plan. Two consequences:

- The clarify session's single **Deferred** item — reconciling with spec-043's then-uncommitted `VisionTimeoutSeconds` — is **resolved**. `VisionTimeoutSeconds = 30` is now committed at HEAD, and both `GeminiBoundaryVisionAnalyzer.cs` and `BoundaryScoringOptions.cs` are clean in the working tree. This plan consumes that option rather than introducing its own.
- The **fail-slow** worst case is already halved: the Gemini vision call is bounded at 30 s instead of inheriting the shared client's 2-minute timeout. Worst case is now Overpass 30 s + ESRI 30 s + vision 30 s ≈ **90 s**, still unbounded in aggregate — so FR-003's 45 s cap is still required.

The **fail-fast** crash path is untouched by `8e83b8f` and verified still present at HEAD: `SendChatMessageCommandHandler.cs:202` still awaits the boundary before the location yield at `:213`; `BoundaryResolutionService.cs:71` still calls `AnalyzeWithVisionAsync` outside any `try`; `AiController.cs:214` still writes `__LOCATION__` after the `await foreach` at `:76`.

## Constitution Check

*GATE: evaluated before Phase 0 and re-evaluated after Phase 1 design.*

| Principle / Section | Assessment | Verdict |
|---|---|---|
| **VIII. No Silent Failures** (NON-NEGOTIABLE) | This feature exists to close a violation. Today an uncaught boundary failure kills the turn with no user-visible outcome — the exact "nothing happened" failure §VIII names. The design catches every boundary/vision failure, logs it with cause, and surfaces a user-visible sentence (FR-005, FR-006). Catch-and-discard is explicitly not used: every catch logs and produces a caller-visible outcome. | **PASS — this is the remediation** |
| **I. Clean Architecture & Dependency Rule** | Changes stay in their layers: Application (handler, `BoundaryResolutionService`), Web (controller flush ordering), Domain (`UserChat.ClearActiveBoundary`). No inward dependency added; no Infrastructure type referenced from Application. | PASS |
| **III. Simplicity First — DRY, KISS, YAGNI** | Smallest change that closes both failure modes. Rejected: background-job delivery (re-architecture), a new SSE channel, retry/circuit-breaker policies. Reuses the existing `BoundaryConfirmationTemplates.Unavailable` sentence rather than writing new copy. | PASS |
| **V. Dependency Inversion & Testability** | Every failure path is reachable through existing interfaces (`IBoundaryResolutionService`, `ISatelliteImageProvider`, `IBoundaryVisionAnalyzer`), so all regression tests are pure substitutes — no network, no clock-skew flakiness beyond one short timeout test. | PASS |
| **VI. Separation of Concerns** | Ordering/emission is the handler's and controller's concern; failure isolation of vision is `BoundaryResolutionService`'s; stored-state lifecycle is the domain entity's. No concern moved into a layer that shouldn't own it. | PASS |
| **§4 Coding Standards — config** | The 45 s cap is bound through `IOptions<BoundaryScoringOptions>` with a `[Range]` attribute, alongside the existing `VisionTimeoutSeconds`. No inline literal. | PASS |
| **§9 AI Principles — bounded invocation** | §9 requires AI invocation be "authorized, logged, and bounded (timeouts, iteration limits)". The aggregate cap plus the committed per-call vision budget satisfies this more completely than today. | PASS |
| **§10 Testing Standards** | FR-010 mandates regression tests for all three paths (throws / hangs / stale state). Behaviour change ⇒ tests, per §18. | PASS |
| **§14 Observability** | Each abandoned boundary logs its cause and elapsed time; the timeout is distinguishable from a provider failure and from caller cancellation (FR-007). | PASS |
| **§15 Performance** | Feature has explicit performance goals (SC-002, SC-007), so §16's performance-review gate applies at merge. | PASS — review required |
| **§5 Database Principles** | No migration. `ActiveBoundary` is an existing owned entity; clearing sets it to null. | PASS — N/A |
| **§7 UI Principles / accessibility** | No user-facing UI change. The client already clears a stale overlay correctly. §16's accessibility gate does not apply. | PASS — N/A |
| **§8 Security** | No auth, data-access, or file-handling surface touched. No new external endpoint. | PASS — N/A |

**Result: no violations. Complexity Tracking section omitted — nothing to justify.**

One tension worth recording rather than hiding: FR-002 requires catching *every* exception type from the boundary step, which is ordinarily an anti-pattern. It is justified here because the step is definitionally optional and the constitution's §VIII bar is met by the catch logging the cause and producing a user-visible outcome — it is isolation, not suppression. Caller cancellation is explicitly re-thrown (FR-007) so the catch-all never swallows a genuine cancellation.

### Post-Phase-1 re-evaluation

Re-checked after `research.md`, `data-model.md`, and `contracts/` were written. **Still no violations.** Three points the design surfaced that were not visible at the pre-Phase-0 check:

- **§III (Simplicity) — duplicated defence.** Phase 0 Decision 3 introduces protection at two layers. This would ordinarily read as redundancy. It survives the gate because the two enforce different invariants at different scopes (vision-is-optional vs turn-integrity), and removing either leaves a demonstrable gap. Recorded in `research.md` rather than left implicit, so a future reader does not "simplify" one away.
- **§VIII — the catch-all is now provably not suppression.** Contract B-1/B-2 pins the permitted exception surface to `OperationCanceledException` alone, and H-3 pins how the two cancellation causes are told apart. Every other path produces a logged cause plus a user-visible sentence. The §VIII bar is met by construction, not by convention.
- **§4 (config) — a new cross-field invariant.** `BoundaryTimeoutSeconds` must exceed `VisionTimeoutSeconds` or vision can never finish inside the aggregate budget. Added to the existing `IValidatableObject.Validate` alongside the weight-sum and threshold-ordering checks, so it fails at startup rather than silently disabling vision in production.

**Gate result: PASS.** Complexity Tracking section remains omitted — nothing requires justification.

## Project Structure

### Documentation (this feature)

```text
specs/044-location-viewer-regression/
├── spec.md              # Feature specification (with Clarifications)
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── chat-stream-events.md   # SSE ordering + boundary-step contract
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Created by /speckit-tasks, not this command
```

### Source Code (repository root)

```text
src/
├── AskLucy.Domain/
│   └── Chats/
│       └── UserChat.cs                          # + ClearActiveBoundary()
├── AskLucy.Application/
│   ├── Ai/Commands/SendChatMessage/
│   │   └── SendChatMessageCommandHandler.cs     # reorder emission; isolate + bound boundary step
│   ├── SiteBoundaries/
│   │   ├── BoundaryResolutionService.cs         # wrap vision path; honour documented contract
│   │   └── BoundaryScoringOptions.cs            # + BoundaryTimeoutSeconds (45)
│   └── Chats/Commands/RecordActiveSiteBoundary/ # + clear path for site change
└── AskLucy.Web/
    ├── Controllers/v1/
    │   └── AiController.cs                      # flush __LOCATION__ mid-stream
    └── appsettings.json                         # BoundaryScoring:BoundaryTimeoutSeconds

tests/
└── AskLucy.Application.Tests/
    ├── Ai/
    │   ├── SendChatMessageBoundaryIntegrationTests.cs   # extend: throws / hangs / stale state
    │   └── SendChatMessageLocationIntegrationTests.cs   # extend: location precedes boundary
    └── SiteBoundaries/
        └── BoundaryResolutionServiceTests.cs            # extend: vision throws / times out
```

**Structure Decision**: Existing Clean Architecture layout, unchanged. The feature touches one file per layer plus configuration — no new project, folder, or architectural seam. The client (`src/AskLucy.Web/ClientApp`) is deliberately **not** in scope: `useChatStream.ts:183` already clears a stale overlay on a mismatched location name, and the SSE parser dispatches by event prefix rather than arrival order, so the reordering needs no client change.
