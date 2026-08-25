# Implementation Plan: Composer Interaction Bug Fixes

**Branch**: `040-composer-interaction-bug-fixes` | **Date**: 2026-08-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/040-composer-interaction-bug-fixes/spec.md`

## Summary

Seven independently-shippable bug fixes discovered during live browser verification of specs/039: four composer control-row layout defects (empty, typing, recording-review, continuous-listening states) that don't match the approved mockups; one functional defect where continuous conversation mode silently fails to start listening before a chat exists; one backend error-classification gap where transcription failures surface a generic unhelpful 500 instead of the existing classified Problem Details response; and one tooltip-placement consistency fix. Each user story is scoped to its own branch/PR, implemented and merged in priority order (US1 → US7) rather than bundled.

## Technical Context

**Language/Version**: TypeScript 5.x (strict) for US1–US5/US7 frontend work; C# / .NET 10 for US6 backend work

**Primary Dependencies**: React 19.2, MUI 9, `@remixicon/react` 4.9 (frontend); ASP.NET Core, MediatR (CQRS) (backend)

**Storage**: N/A — no data model changes; all fixes are presentational/control-flow/error-classification

**Testing**: Vitest 4 + `@testing-library/react` 16 (frontend); xUnit-style handler/middleware tests under `tests/AskLucy.Web.Tests` (backend)

**Target Platform**: Web (existing Ask Lucy AI Workspace, `src/AskLucy.Web/ClientApp` served from `src/AskLucy.Web`)

**Project Type**: Web application (existing ASP.NET Core backend + React/TS frontend, Clean Architecture)

**Performance Goals**: N/A — no new performance-sensitive code paths; purely layout reordering, one added `useEffect`/waveform element, and backend exception classification

**Constraints**: Must preserve every existing behavior called out in spec.md's Assumptions (specs/033's pointer-capture single-element invariant for the mic button; specs/039's composer visual-state model; existing retry-before-classify behavior for transient AI-provider failures)

**Scale/Scope**: 7 user stories touching `ChatComposer.tsx`, `RecordingReviewControls.tsx`, `CollapsedVoiceControls.tsx`, `ChatPage.tsx` (frontend) and `OpenAIProvider.cs`, `ProblemDetailsMiddleware.cs` (backend); no new files expected except tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Clean Architecture Dependency Rule**: PASS — US1–US5/US7 stay entirely within the React feature folder (`features/chat/components`, `features/chat/pages`); US6 stays within `AskLucy.Infrastructure` (provider) and `AskLucy.Web` (middleware), matching the existing layering (Infrastructure implements `IAIProvider`, Web hosts the global exception middleware). No new cross-layer references introduced.
- **No Silent Failures (§2.VIII, non-negotiable)**: DIRECTLY MOTIVATES US5 and US6 — both fix real silent-failure gaps (continuous mode's no-op capture-start, and the unmapped `HttpRequestException`/unvalidated-credential path collapsing into a generic 500). PASS, and this feature strictly improves compliance versus the current state.
- **Git Workflow (§11)**: PASS — each user story ships on its own `<###-feature-slug>`-style branch (or a per-story sub-branch off this feature's numbering, decided at implementation time to satisfy "push and merge one at a time"), Conventional Commits, squash-merged, matching this repo's established convention ([[cicd_workflow_repo_conventions]]).
- **CI/CD (§12)**: PASS — no change to the pipeline itself; each story's PR runs the existing `backend-build-and-test`/`frontend-build-lint-and-test` CI jobs unchanged.
- **Voice output persona requirement**: N/A — this feature doesn't touch TTS voice selection.
- **Testability**: PASS — every user story's acceptance scenarios in spec.md are directly expressible as component/unit tests against the existing test suites (`ChatComposer.test.tsx`, `ChatPage.test.tsx`, a new/extended backend test for US6's classification).

No violations requiring justification — Complexity Tracking table is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/040-composer-interaction-bug-fixes/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/features/chat/
├── components/
│   ├── ChatComposer.tsx              # US1, US2, US3, US4 — control-row layout per state
│   ├── ChatComposer.test.tsx
│   ├── RecordingReviewControls.tsx   # US3 (order) + US7 (tooltip placement)
│   ├── RecordingReviewControls.test.tsx   # new — currently untested standalone
│   └── CollapsedVoiceControls.tsx    # US7 (tooltip placement)
└── pages/
    ├── ChatPage.tsx                  # US5 — continuous-mode capture-start reliability
    └── ChatPage.test.tsx

src/AskLucy.Infrastructure/Ai/
└── OpenAIProvider.cs                 # US6 — credential validation, exception classification

src/AskLucy.Web/Middleware/
└── ProblemDetailsMiddleware.cs       # US6 — HttpRequestException mapping

tests/AskLucy.Infrastructure.Tests/Ai/
└── OpenAIProviderTests.cs            # US6 backend tests (extend or create)

tests/AskLucy.Web.Tests/Middleware/
└── ProblemDetailsMiddlewareTests.cs  # US6 backend tests (extend)
```

**Structure Decision**: Existing Clean Architecture layout is reused as-is — no new projects, folders, or architectural layers. Frontend fixes (US1–US5, US7) live entirely in the existing `features/chat` folder already established by specs/026/029/030/031/039. The backend fix (US6) lives in the existing `AskLucy.Infrastructure`/`AskLucy.Web` split already established by specs/005/032.

## Complexity Tracking

*No violations — table omitted.*
