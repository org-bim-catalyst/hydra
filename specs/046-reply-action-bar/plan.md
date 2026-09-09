# Implementation Plan: Reply Action Bar

**Branch**: `main` (solo-developer workflow — no feature branch, see [[feedback_push_directly_to_main]]) | **Date**: 2026-09-09 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/046-reply-action-bar/spec.md`

## Summary

Move the assistant-reply Replay/Stop control out of the message bubble's absolutely-positioned
lower-right corner into a normal-flow action row rendered beneath the bubble, add a new Copy
action to that row, and shrink both icons — matching the ChatGPT/Claude convention. This is a
UI-only change confined to `MessageBubble.tsx` (rendering) and `ChatPage.tsx` (wiring a new
`onCopy`-adjacent confirmation surface); it reuses the existing `@remixicon/react` icon set and
the existing replay/stop state machine from specs/039-composer-interaction-states-redesign
unchanged (FR-008).

## Technical Context

**Language/Version**: TypeScript 5, React 18

**Primary Dependencies**: MUI (Material UI), `@remixicon/react`, existing `useVoiceOutput` hook (unchanged)

**Storage**: N/A — no persistence; clipboard write only

**Testing**: Vitest + React Testing Library (existing `MessageBubble.test.tsx`, `MessageBubble.a11y.test.tsx`, `ChatPage.test.tsx`)

**Target Platform**: Web (ClientApp SPA), all currently-supported browsers with the async Clipboard API

**Project Type**: Web application (existing ASP.NET Core + React SPA) — this feature touches only `frontend` (`src/AskLucy.Web/ClientApp`)

**Performance Goals**: N/A beyond existing render performance; copy confirmation must appear within 1s (SC-001)

**Constraints**: No silent failures (CLAUDE.md Error Handling / constitution); must not regress any specs/039 US5 acceptance scenario

**Scale/Scope**: Single component (`MessageBubble.tsx`) plus its direct caller (`ChatPage.tsx`); no new routes, no new backend endpoints, no schema change

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Clean Architecture / Dependency Rule**: N/A — pure presentation-layer change inside the
  React SPA; no Domain/Application/Infrastructure code touched. PASS.
- **Error Handling (no silent failures)**: Directly governs this feature — FR-002/FR-003
  require the clipboard write to always produce a visible outcome. The plan uses the async
  Clipboard API's own promise rejection to drive a visible failure state (never a
  fire-and-forget call). PASS (see research.md Decision 2).
- **Provider neutrality / AI vendor abstraction**: N/A — no AI provider code touched.
- **Security**: Clipboard write uses only the text already rendered to the user in the DOM
  (`message.content`); no new data leaves the client, no new endpoint, no new input surface.
  PASS.

No violations. Complexity Tracking table not needed.

## Project Structure

### Documentation (this feature)

```text
specs/046-reply-action-bar/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── reply-action-bar.md
└── tasks.md              # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/
├── src/
│   └── features/
│       └── chat/
│           ├── components/
│           │   ├── MessageBubble.tsx            # MODIFIED — action row, Copy action, smaller icons
│           │   ├── MessageBubble.test.tsx        # MODIFIED — new/updated assertions
│           │   └── MessageBubble.a11y.test.tsx   # MODIFIED — updated a11y assertions for new control
│           └── pages/
│               ├── ChatPage.tsx                  # UNCHANGED (replay wiring already passes through)
│               └── ChatPage.test.tsx             # MODIFIED if bubble-level assertions need updating
```

**Structure Decision**: Existing single web-app structure (`src/AskLucy.Web` backend +
`src/AskLucy.Web/ClientApp` React SPA) is unchanged. This feature is entirely inside
`ClientApp/src/features/chat/components/MessageBubble.tsx`; no new files, directories, or
cross-cutting modules are introduced.

## Complexity Tracking

Not applicable — no constitution violations.
