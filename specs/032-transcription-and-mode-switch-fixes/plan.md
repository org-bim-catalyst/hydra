# Implementation Plan: Transcription 500 Fix & Mode-Switch Simplification

**Branch**: `032-transcription-and-mode-switch-fixes` | **Date**: 2026-08-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/032-transcription-and-mode-switch-fixes/spec.md`

## Summary

Classify the previously-unclassified 4xx response `OpenAIProvider.EnsureSuccessAsync` lets fall
through to a generic 500 as a new `AiProviderRequestInvalidException`, mapped by
`ProblemDetailsMiddleware` to a real 400 with an actionable detail message; fix the frontend's
`transcribeAudio` to actually surface that detail (today it discards the response body and shows
only the raw status code) and fix `useVoiceRecorder.ts`'s hardcoded `'recording.webm'` filename
(a concrete, code-identified trigger for OpenAI's 400) to reflect the real recorded blob's MIME
type. Separately, remove the two-click mode-switch dropdown menu in `ChatComposer.tsx` in favor
of a direct single-click toggle. The Push-to-Talk hold gesture itself is unchanged — regression-
verified only.

## Technical Context

**Language/Version**: C# / .NET 10 (backend), TypeScript 5.x / React 19 (frontend)

**Primary Dependencies**: Backend — MediatR, the existing `IAIProvider` abstraction
(`AskLucy.Application.Abstractions`), ASP.NET Core's exception-handling middleware. Frontend —
MUI (`IconButton`, removing `Menu`/`MenuItem`), the existing `ApiError`/`apiFetch` convention in
`src/api/httpClient.ts`.

**Storage**: N/A — no schema/data changes.

**Testing**: xUnit + FluentAssertions (backend, mirroring `OpenAIProvider`'s existing test
conventions — no dedicated `OpenAIProviderTests.cs` currently exists per the investigation, so
this feature adds the first one; the new middleware-mapping case is also covered in a new file
rather than the pre-existing-dirty `ProblemDetailsMiddlewareTests.cs`, per research.md Decision 5),
Vitest + React Testing Library (frontend, existing conventions).

**Target Platform**: ASP.NET Core Web API (`AskLucy.Web`) + React SPA
(`AskLucy.Web/ClientApp`) — the first feature in this chat-widget bug-fix series (specs/029-031)
to touch both layers.

**Project Type**: Web application — full-stack fix within the existing Clean Architecture
solution (`AskLucy.Domain` → `AskLucy.Application` → `AskLucy.Infrastructure` → `AskLucy.Web`)
plus the frontend SPA.

**Performance Goals**: No new latency — this is error-classification and a UI interaction
simplification, not a new code path in the success case.

**Constraints**: Must not alter the existing, already-correct 401/403 → `AiProviderAuthenticationException`
(502) and 429 → `AiProviderRateLimitedException` (429) classification or their retry/no-retry
behavior in `WithRetryAsync` (spec.md FR-005). Must follow constitution §2.VIII (no silent
failures) — the new exception type and its mapping are exactly this principle applied to a gap
the investigation found. Must reuse the existing `AiProvider*Exception`/Problem Details pattern
(constitution §7 "Convention over Configuration") rather than inventing a new error-handling
mechanism.

**Scale/Scope**: One new exception type, one new `EnsureSuccessAsync` branch, one new
`ProblemDetailsMiddleware` case (backend); one `aiApi.ts` fix, one `useVoiceRecorder.ts` fix, one
`ChatComposer.tsx` simplification (frontend); associated tests on both sides. No new
endpoints, no new pages, no database changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|------|--------|-------|
| §1 Clean Architecture / Dependency Rule | PASS | New exception lives in `AskLucy.Application.Abstractions` (where its three siblings already live); thrown from `AskLucy.Infrastructure.Ai.OpenAIProvider`; mapped in `AskLucy.Web`'s middleware. Dependencies point inward only, matching the existing pattern exactly. |
| §2.VIII No Silent Failures (NON-NEGOTIABLE) | PASS | This feature exists specifically to close a silent-failure gap: an unclassified 4xx currently defaults to an opaque 500 with no actionable detail reaching the user. Both the backend classification and the frontend detail-surfacing fix are required together to fully satisfy this principle for this endpoint. |
| §3 Simplicity / DRY / YAGNI | PASS | Reuses the existing `AiProvider*Exception` → `ProblemDetailsMiddleware` pattern and the existing `ApiError` frontend convention — no new abstraction invented. Mode-switch fix removes a component (`Menu`/`MenuItem`) rather than adding one. |
| §6 API Standards — Problem Details | PASS | The new exception maps to a proper RFC 7807 Problem Details response via the same `Map()` switch every other domain/provider exception already uses — no ad hoc error shape introduced. |
| §7 UI Principles — accessibility, design system reuse | PASS | Mode-switch fix keeps the existing `IconButton` + `Tooltip` (per specs/030's tooltip requirement); removes `Menu`/`MenuItem`, adds no new component. |
| §10 Testing Standards | PASS (planned in tasks) | New xUnit tests for `OpenAIProvider`'s 4xx classification (a genuine coverage gap the investigation confirmed) and `ProblemDetailsMiddleware`'s new mapping; new/updated Vitest tests for `transcribeAudio`'s detail-surfacing, `useVoiceRecorder.ts`'s filename fix, and `ChatComposer.tsx`'s single-click mode toggle. |
| §16 Quality Gates | PASS (planned) | No architecture violations; tests accompany every behavior change; no accessibility regression (mode-switch button keeps its existing label/tooltip, just changes its click behavior). |

No violations identified — Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/032-transcription-and-mode-switch-fixes/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── AskLucy.Application/
│   └── Abstractions/
│       └── IAIProvider.cs                      # MODIFIED: add AiProviderRequestInvalidException
├── AskLucy.Infrastructure/
│   └── Ai/
│       └── OpenAIProvider.cs                   # MODIFIED: EnsureSuccessAsync classifies other 4xx
├── AskLucy.Web/
│   ├── Middleware/
│   │   └── ProblemDetailsMiddleware.cs         # MODIFIED: new Map() case, 400
│   └── ClientApp/src/
│       ├── api/
│       │   └── httpClient.ts                   # referenced only (ApiError already exists here)
│       └── features/chat/
│           ├── api/
│           │   └── aiApi.ts                    # MODIFIED: transcribeAudio surfaces ApiError.detail
│           ├── voice/
│           │   ├── useVoiceRecorder.ts         # MODIFIED: filename derived from blob.type
│           │   └── useVoiceRecorder.test.ts    # MODIFIED
│           └── components/
│               ├── ChatComposer.tsx            # MODIFIED: mode-switch single click, no Menu
│               └── ChatComposer.test.tsx       # MODIFIED
tests/
├── AskLucy.Infrastructure.Tests/
│   └── Ai/
│       └── OpenAIProviderTests.cs              # NEW: first test file for this provider
└── AskLucy.Web.Tests/
    └── Middleware/
        └── AiProviderRequestInvalidExceptionMappingTests.cs  # NEW (see research.md Decision 5)
```

**Structure Decision**: Extend the existing `AiProvider*Exception` family and
`ProblemDetailsMiddleware.Map()` switch in place (backend); extend the existing
`ApiError`/`apiFetch` convention and the two already-modified voice-flow files from
specs/031 in place (frontend). Backend test coverage goes into **two new files**, not the
existing `ProblemDetailsMiddlewareTests.cs`/`tests/AskLucy.Web.Tests/Ai/*` — every existing
candidate file there already carries an unrelated, pre-existing uncommitted change (a
repo-wide mechanical `cancellationToken` migration), and editing any of them would bundle
that unrelated diff into this feature's commit (research.md Decision 5).

## Complexity Tracking

*No Constitution Check violations — this section is intentionally empty.*
