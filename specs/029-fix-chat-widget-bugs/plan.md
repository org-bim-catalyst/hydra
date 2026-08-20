# Implementation Plan: Chat Widget Reliability & Voice UI Consolidation

**Branch**: `029-fix-chat-widget-bugs` | **Date**: 2026-08-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/029-fix-chat-widget-bugs/spec.md`

## Summary

Four production bugs in the Ask Lucy chat widget, all traced to root cause during
research: (1) `GET /api/v1/ai/voice/preferences` 500s — almost certainly an unapplied
EF Core migration (`AddUserVoicePreferenceDefaultLanguage`) causing schema drift — and
the failure surfaces as an alarming Snackbar on every chat load even though the store
already degrades to safe defaults; (2) the Expanded chat panel renders **two** independent
mic/recording UIs simultaneously (`VoiceControlBar` and `ChatComposer`'s own inline
controls), both driven by the same `recorder`/`recognition` state, because they were
composed as unconnected siblings; (3) the translate control sits in its own header-style
toolbar row above the message list, which the user has asked to move into the
composer/voice-control row; (4) a hand-rolled SPA-fallback middleware in `Program.cs`
intercepts GET requests to all 6 SignalR hub paths (`/hubs/*`) before they reach
`MapHub`, because its manual prefix-exclusion list only knows about `/api`, `/openapi`,
and `/health`.

Technical approach: apply the missing migration and add a narrowly-scoped EF
pending-migrations readiness check; consolidate the duplicated voice UI into
`ChatComposer` alone (retiring `VoiceControlBar` from the Expanded panel only —
`CollapsedVoiceControls` already does this correctly and is unaffected), merging the
former separate speaker-mute and stop-current-reply controls into one always-visible
icon per explicit direction, with the "Lucy is speaking…" text dropped outright since
`AiPresenceCard`'s persistent reactive presence indicator already covers it elsewhere on
the workspace; relocate the
translate icon into the composer row while leaving `ProjectPicker` where spec 026 already
anchored it; and convert the SPA-fallback's index.html branch into an `app.MapFallback`
endpoint registered after `MapControllers`/`MapHub`, so endpoint-routing precedence
(not a maintained exclusion list) is what protects hub/controller/health/OpenAPI routes
going forward.

## Technical Context

**Language/Version**: C# / .NET 10 (backend), TypeScript 5 / React 19 (frontend) — existing stack, unchanged.

**Primary Dependencies**: ASP.NET Core, EF Core, MediatR, SignalR, Serilog (backend); Vite, MUI, Zustand, TanStack Query, `@remixicon/react` (frontend) — all pre-existing in this codebase; no new dependency is introduced by this feature.

**Storage**: SQL Server via EF Core — no schema change beyond applying the already-authored, already-committed `20260817110019_AddUserVoicePreferenceDefaultLanguage` migration (no new migration is authored by this feature).

**Testing**: xUnit (backend unit/integration, per constitution §10), Vitest + Testing Library (frontend, matching existing `*.test.tsx`/`*.a11y.test.tsx` files in `src/AskLucy.Web/ClientApp/src/features/chat`).

**Target Platform**: Existing deployed web app (hydra.bimcatalyst.com) — ASP.NET Core host serving the built React SPA from `wwwroot`.

**Project Type**: Web application (backend + frontend in one repo) — existing structure, no new projects.

**Performance Goals**: No new performance goal introduced; SC-003 (99% first-attempt real-time connection success) is a reliability target, not a throughput/latency target.

**Constraints**: Must not touch the hand-rolled static-file-serving logic in `Program.cs` (lines 493–517) — it exists because the built-in `StaticFileMiddleware`/`MapFallbackToFile` demonstrably failed to serve PreBuildEvent-copied `wwwroot` assets in this exact deployment (documented in the code comment at `Program.cs:483-492`); only the SPA *fallback* branch (index.html) may be restructured. Must not relocate `ProjectPicker` — spec 026's `contracts/chat-widget-components.md:108` explicitly anchored it (and Translate) in `ConversationView`'s own toolbar as a deliberate design boundary between that toolbar and `ExpandedChatPanel`'s identity header.

**Scale/Scope**: Single feature, 4 independent-but-related fixes, touching ~10 backend files (1 new health check, `Program.cs`, migration application) and ~6 frontend files (`ChatPage.tsx`, `ChatComposer.tsx`, retirement of `VoiceControlBar` from the Expanded tree, `voicePreferencesStore.ts`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Principle I (Clean Architecture)** — PASS. All backend changes stay within existing layer boundaries: the health check lives in `Infrastructure` (or `Api` composition root) and depends only on `AskLucyDbContext` via `Database.GetPendingMigrationsAsync()`; no Domain/Application code changes; `Program.cs` changes are Composition Root only.
- **Principle VIII (No Silent Failures, NON-NEGOTIABLE)** — flagged and resolved, not violated, in two independent places this feature touches:
  1. FR-001 asks for no "alarming" banner on every chat load; naively satisfying that by removing the Snackbar entirely would produce a fully silent frontend failure, forbidden by this NON-NEGOTIABLE principle ("every async operation that can fail... MUST have an explicit error path that reaches the user through visible UI feedback"). **Resolution** (Phase 0 Decision 3): replace the blocking `severity="error" variant="filled"` Snackbar with a small, non-blocking, dismissible inline indicator scoped to the voice-settings area (not a full-width alert firing on chat load) — this satisfies FR-001 (not scary, not on every load's focal point) and Principle VIII (still visible, still user-facing, never console-only) simultaneously. Server-side, the failure is already logged by `ProblemDetailsMiddleware` today (verified in research) — FR-003's traceability requirement is already met and does not require new backend logging.
  2. `/speckit-analyze` (post-tasks review, finding C1/G1) caught a real instance of this exact principle being violated today, missed in the original Phase 0/1 pass: `useFloatingPanelHub.ts` (the hub from the original bug report), `useMemoryNotificationsHub.ts`, and `useNotificationHub.ts` each call `connection.start().catch(() => undefined)` — discarding a connection failure with zero user-visible trace, the literal pattern §2.VIII names as forbidden. Three sibling hooks in the same codebase (`useWorkflowExecutionHub`, `useDocumentProcessingHub`, `useAgentExecutionHub`) already implement the correct fix: expose `{ isLive: boolean }`, wired to `onreconnected`/`onreconnecting`/`onclose` and `connection.start().then(success, failure)`. **Resolution**: apply that already-established pattern to the 3 non-compliant hooks (tasks.md T004a-T004d) rather than inventing a new mechanism — reuse over new abstraction, per §2.III.
- **§5 Migrations** — PASS. No new migration authored; the fix applies an existing, already-committed, already-reversible migration. The FR-012 safeguard reuses EF Core's own `GetPendingMigrationsAsync()` bookkeeping rather than inventing a bespoke schema-diff mechanism (respects §2.III YAGNI/KISS).
- **§14 Observability** — the FR-012 safeguard is implemented as a `/health/ready` check, which constitution §14 already specifies as the expected endpoint shape ("readiness, checking DB/provider connectivity") but which does not yet exist in this codebase (only `/health` liveness exists today). This feature adds the first readiness check under that documented contract, scoped to what FR-012 needs — it does not attempt to build out full readiness coverage for every dependency, which is out of this feature's scope.
- **§7 UI Principles — state management** — noted, not a blocker. `voicePreferencesStore.ts` currently fetches server data into a Zustand store via a hand-rolled `try/catch`+`fetch`, which is exactly the pattern §7 reserves for TanStack Query ("server state... lives in TanStack Query and MUST NOT be duplicated into Zustand") and is *also* the direct cause of Bug 1's error-handling gap (no retry, no built-in error state separation, easy to wire straight into a blocking Snackbar). Since this file must be touched anyway to fix FR-001, Phase 0 Decision 4 migrates the fetch itself to TanStack Query's `useQuery` (matching the sibling `useAiPreferences` hook's existing pattern in this codebase) and narrows the Zustand store to the client-only fields it should own (the cached preference values used synchronously elsewhere in the render tree, and any local UI-only flags) — bringing this file into compliance as a side effect of the bug fix, not a separate refactor.
- **§7 UI Principles — accessibility** — PASS, inherited. WCAG 2.1 AA (keyboard operability, ARIA labels, focus states) applies uniformly to the consolidated mic control per the existing constitutional blanket rule; no feature-specific exception is introduced. The consolidated control reuses `ChatComposer`'s already-implemented keyboard path (`onKeyDown`/`onKeyUp` Space-bar hold-to-record handling) rather than inventing a new one.
- **§16 Quality Gates** — accessibility review and architecture-compliance review apply to this change per the standard gate; no gate is marked not-applicable.

No unjustified violations. Complexity Tracking is not needed — no principle is being knowingly broken, only reconciled via the design decisions above.

**Post-Phase 1 re-check**: Confirmed against the finished `data-model.md` and
`contracts/`. The `expanded-voice-control-consolidation.md` contract keeps
`CollapsedVoiceControls` and its shared-contract role in spec 026 untouched (no
Dependency Rule or cross-feature contract violation); the `health-readiness-endpoint.md`
contract adds one new, additive, unauthenticated endpoint consistent with `/health`'s
existing convention (§6 API Standards); no new persisted entity or migration is
introduced (§5). No new violations surfaced during design — gate still PASSES.

## Project Structure

### Documentation (this feature)

```text
specs/029-fix-chat-widget-bugs/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/
│   ├── health-readiness-endpoint.md
│   └── expanded-voice-control-consolidation.md
└── tasks.md              # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

Existing web application structure (backend + frontend in one repo, ASP.NET Core hosting a
built React SPA) — no new projects or top-level directories are introduced.

```text
src/
├── AskLucy.Domain/
│   └── Ai/UserVoicePreference.cs                       # unchanged (already has DefaultLanguage)
├── AskLucy.Application/
│   └── Ai/Queries/GetUserVoicePreference/
│       └── GetUserVoicePreferenceQueryHandler.cs        # unchanged (already null-safe)
├── AskLucy.Infrastructure/
│   └── Panels/PanelHub.cs                                # unchanged (already correctly mapped)
├── AskLucy.Persistence/
│   ├── HealthChecks/
│   │   └── PendingMigrationsHealthCheck.cs              # NEW — FR-012. Relocated here during
│   │                                                     #   implementation from the AskLucy.Infrastructure
│   │                                                     #   path originally planned above:
│   │                                                     #   AskLucy.Infrastructure has no real (non-comment)
│   │                                                     #   reference to AskLucy.Persistence anywhere in the
│   │                                                     #   codebase, and an existing comment there already
│   │                                                     #   documents that AskLucyDbContext-dependent code
│   │                                                     #   belongs in Persistence — followed that convention
│   │                                                     #   instead of introducing a new cross-project edge.
│   ├── Migrations/20260817110019_AddUserVoicePreferenceDefaultLanguage.cs  # apply, don't author
│   └── Repositories/UserVoicePreferenceRepository.cs     # unchanged
├── AskLucy.Web/
│   ├── Program.cs                                        # MODIFY — health check registration, MapFallback reorder (Bug 4), FR-012 wiring
│   ├── Middleware/ProblemDetailsMiddleware.cs             # unchanged (already logs unhandled exceptions)
│   └── ClientApp/src/
│       ├── features/chat/
│       │   ├── pages/ChatPage.tsx                        # MODIFY — retire VoiceControlBar from Expanded tree, relocate translate icon, tighten toolbar row
│       │   ├── components/
│       │   │   ├── ChatComposer.tsx                      # MODIFY — becomes the single consolidated voice-control home
│       │   │   ├── VoiceControlBar.tsx                    # DELETED — confirmed during implementation that only its own test file referenced it; CollapsedVoiceControls (unaffected) has its own independent implementation, not a dependency on this file
│       │   │   └── RecordingReviewControls.tsx             # unchanged, reused as-is
│       │   ├── voice/
│       │   │   ├── voicePreferencesStore.ts                # MODIFY — TanStack Query for the fetch, narrowed Zustand slice, non-alarming fallback indicator
│       │   │   └── useVoiceRecorder.ts                     # unchanged
│       │   └── api/voiceApi.ts                             # unchanged (same endpoint/shape)
│       ├── api/httpClient.ts                               # unchanged (Problem Details → ApiError mapping already correct)
│       ├── viewer/panels/hooks/useFloatingPanelHub.ts       # MODIFY — expose isLive per the 3 already-compliant hub hooks (FR-010, C1)
│       ├── features/viewer/components/ViewerSurface.tsx     # MODIFY — render the new isLive Live/Reconnecting indicator
│       ├── features/memory/hooks/useMemoryNotificationsHub.ts  # MODIFY — same isLive fix as useFloatingPanelHub
│       ├── features/memory/pages/MemoryCenterPage.tsx       # MODIFY — consume isLive
│       ├── features/documents/hooks/useNotificationHub.ts   # MODIFY — same isLive fix (distinct from the already-compliant useDocumentProcessingHub)
│       └── features/documents/pages/DocumentWorkspacePage.tsx  # MODIFY — consume isLive
```

**Structure Decision**: No structural change to the existing Clean Architecture layering or the frontend's feature-folder convention (`src/features/<domain>`). All work lands inside the existing `AskLucy.Web` (API host + Composition Root) and its `ClientApp` frontend, plus one new `Infrastructure` health check class — the smallest surface consistent with each bug's actual root cause.
