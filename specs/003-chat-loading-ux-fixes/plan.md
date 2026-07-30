# Implementation Plan: Chat Loading & Reply Feedback Fixes

**Branch**: `003-chat-loading-ux-fixes` | **Date**: 2026-07-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-chat-loading-ux-fixes/spec.md`

## Summary

`ConversationView` (in `ChatPage.tsx`) currently derives its "no conversation" empty state
purely from `messages.length === 0`, without ever inspecting `useChatMessages`' own
loading/error status. Because `useChatStream` seeds `messages` from `undefined` until the
first page of persisted messages resolves, a selected conversation renders the "Start a
conversation with Ask Lucy." placeholder for the entire duration of its fetch — this is
Bug #1/#3. The fix threads TanStack Query's existing `isPending`/`isError`/`error`/`refetch`
state for the selected chat's message query into three explicit, mutually exclusive render
branches (loading spinner / error-with-retry / content), and reserves the empty-state copy
for the one case it actually describes: no conversation selected. A small three-dot
"thinking" indicator component is added and shown in place of the assistant's placeholder
bubble in `useChatStream.send` while no streamed content has arrived yet (Bug #2), replacing
the current silent Snackbar-only failure with an indicator-area retry affordance on send
failure (Bug #2's error path). The provider/model attribution caption in `MessageBubble` is
removed from render output while the underlying `provider`/`model` fields are left on the
`ChatMessage`/`PersistedMessage` types untouched, since they still flow into
`useTextToSpeech`/analytics-adjacent code paths unaffected by this change (Bug #4). All four
fixes are frontend-only, confined to `src/AskLucy.Web/ClientApp/src/features/chat/`, with no
backend, contract, or data-model changes.

**Amendment (2026-07-30, User Story 5)**: Post-release manual testing surfaced a fifth bug —
reopening a conversation created and used earlier in the session (via the auto-create-on-send
flow) shows a permanently blank pane instead of its real messages. Root cause (`research.md`
Topic 6): `useChatStream`'s message-seeding effect only ever applied fetched data *once*,
gated by whether `initialMessages` was merely *defined* — and a brand-new chat's first
messages fetch races an in-progress reply, resolving empty and getting cached by TanStack
Query. On the next mount, that stale-but-defined empty snapshot permanently blocks the later,
corrected background refetch from ever being applied. Fixed by replacing the `initializedRef`
"seed once" latch with a `hasSentRef` gate that only flips once the user actually sends in
that view, so the seeding effect keeps tracking the query's data (including corrections and
later paginated pages) until then. This is the same `useChatStream.ts` file already touched
for Bug #2/#3; no new files or architectural surface are introduced by this amendment.

## Technical Context

**Language/Version**: TypeScript ~6.0 (frontend, strict mode), targeting the existing React 19 SPA. No backend (.NET 10) changes are required for this feature.

**Primary Dependencies**: React 19, MUI (`@mui/material` v9 — `CircularProgress`, `Alert`, `Button`), TanStack Query v5 (`useInfiniteQuery` status flags: `isPending`, `isError`, `error`, `refetch`), TanStack Virtual v3 (existing message/list virtualization, unaffected).

**Storage**: N/A — no schema, entity, or API contract changes. This feature only changes what the existing `useChatMessages`/`useChatStream` data is used to render.

**Testing**: Vitest + React Testing Library (`ChatPage`/`ConversationView`, `MessageBubble`, `useChatStream` unit/component tests), `jest-axe` for the accessibility checks the constitution requires (§7, §10) on the new loading/error/thinking-indicator UI, following the existing pattern in `ChatSidebar.a11y.test.tsx`.

**Target Platform**: Existing web SPA (all supported browsers/breakpoints for Ask Lucy's chat UI).

**Project Type**: Web application (ASP.NET Core backend + Vite/React frontend, already established under `src/AskLucy.Web/ClientApp`). This feature is frontend-only.

**Performance Goals**: Loading spinner and thinking indicator visible within 100ms of the triggering click/send (spec SC-002/SC-003) — both are synchronous local-state transitions (query `isPending` is already `true` the instant a new query key mounts; `isStreaming`/placeholder-bubble insertion in `useChatStream.send` is synchronous), so no debouncing or artificial delay is introduced.

**Constraints**: No new npm dependencies expected — MUI already provides `CircularProgress`; the three-dot "thinking" indicator is a small custom component (CSS/MUI `keyframes`-based), justified under constitution §7 as a foundational, reusable messaging-UI primitive rather than a one-off. No enforced minimum display duration for either indicator (spec clarification). No reduced-motion fallback variant required (spec clarification) — still meets WCAG 2.1 AA, since respecting `prefers-reduced-motion` for non-flashing loading affordances is a AAA-level (2.3.3), not AA-level, success criterion.

**Scale/Scope**: 3 existing components/hooks modified (`ChatPage.tsx`'s `ConversationView`, `MessageBubble.tsx`, `useChatStream.ts` — the latter touched twice, once for the retry mechanism and once for the message-sync gate fix), 1 new small presentational component added (`ThinkingIndicator`), plus corresponding test updates — no new routes, pages, or backend surface.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Rule | Assessment |
|---|---|
| VIII. No Silent Failures (§2) | **Directly satisfied by this feature.** FR-004/FR-008 replace today's silent-until-fetch-resolves gap and the Snackbar-only send failure with explicit, user-visible error states carrying a manual "Retry" action — this is the core of what the feature fixes, not a new risk it introduces. |
| VI. Separation of Concerns | Loading/error/thinking-indicator logic is presentation-layer only (React components/hooks), reading state TanStack Query and `useChatStream` already own — no business logic added to components. |
| §7 UI Principles — Design system | `CircularProgress`/`Alert`/`Button` come from the existing MUI theme (no bespoke spinner/alert built). The new `ThinkingIndicator` is the one genuinely new visual primitive; justified as foundational (a typing/thinking indicator is a standard, broadly-reusable messaging-UI pattern), not a one-off for a single call site. |
| §7 UI Principles — Accessibility (WCAG 2.1 AA) | New loading/error/thinking states MUST carry correct ARIA roles (`role="status"`/`aria-live="polite"` for the spinner and thinking indicator, `role="alert"` for the error state) and be covered by an axe check, matching the existing `ChatSidebar.a11y.test.tsx` pattern. Skipping a reduced-motion fallback does not violate AA (§ Constraints above). |
| §7 UI Principles — State management | Loading/error state is read directly from TanStack Query's own status (`isPending`/`isError`/`error`), not duplicated into Zustand — consistent with the constitution's "server state lives in TanStack Query" rule. |
| §3 Architecture Rules (Clean Architecture) | Not implicated — no Domain/Application/Infrastructure/Api changes; all work is in the `Frontend`/`ClientApp` presentation layer, which already communicates with the backend only via its public HTTP API. |
| §10 Testing Standards | New/changed behavior gets test coverage in the same change: `ConversationView` loading/error/content branch rendering, `ThinkingIndicator` appearance/removal timing, `MessageBubble` no longer rendering attribution (existing test at `MessageBubble.test.tsx:16-19` asserting the opposite must be updated, not left contradicting the new behavior), and an a11y check for the new states. User Story 5's fix ships with two regression tests (T030/T031) — one verified to fail against the pre-fix code (confirmed by temporarily reverting the fix and re-running), one covering the beneficial pagination side effect. |
| VIII. No Silent Failures (§2) — User Story 5 | Also directly satisfied: a conversation silently, permanently showing zero messages when it actually has content is exactly the class of failure this principle forbids — the fix makes the view eventually consistent with the true server state instead of silently discarding a corrected background refetch. |

**Result**: PASS — no unjustified violations. No entries required in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/003-chat-loading-ux-fixes/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command) — UI state models, no DB entities
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify + /speckit-clarify)
└── tasks.md             # Phase 2 output (/speckit-tasks command — NOT created by /speckit-plan)
```

No `contracts/` directory is generated for this feature: it introduces no new or changed
backend endpoint, message schema, or other externally-consumed interface — every change is
confined to how already-fetched data is rendered in the existing frontend.

### Source Code (repository root)

```text
src/
├── AskLucy.Domain/            # untouched
├── AskLucy.Application/       # untouched
├── AskLucy.Infrastructure/    # untouched
├── AskLucy.Persistence/       # untouched
└── AskLucy.Web/
    ├── Controllers/           # untouched (AiController.cs SSE endpoint already supports this UI)
    └── ClientApp/
        └── src/
            └── features/
                └── chat/
                    ├── pages/
                    │   ├── ChatPage.tsx           # MODIFY: ConversationView loading/error/empty branches
                    │   ├── ChatPage.test.tsx      # NEW: loading/error/empty branch + thinking-indicator tests
                    │   └── ChatPage.a11y.test.tsx # NEW: jest-axe coverage for loading + error/Retry states
                    ├── components/
                    │   ├── MessageBubble.tsx       # MODIFY: remove provider/model attribution caption
                    │   ├── MessageBubble.test.tsx  # MODIFY: update/replace attribution-rendering test
                    │   ├── ThinkingIndicator.tsx   # NEW: three-dot animated "thinking" indicator
                    │   └── ThinkingIndicator.test.tsx  # NEW: incl. jest-axe coverage
                    └── hooks/
                        ├── useChats.ts             # unchanged (already exposes isPending/isError/refetch via useInfiniteQuery)
                        └── useChatStream.ts        # MODIFY: expose thinking/error-with-retry state for the in-flight send

tests/                          # backend tests — untouched (no backend changes in this feature)
```

**Structure Decision**: This is the existing Web application structure (ASP.NET Core
backend `src/AskLucy.Web` + Vite/React frontend at `src/AskLucy.Web/ClientApp`). This
feature only adds/modifies files under `ClientApp/src/features/chat/`; no new top-level
directories are introduced.

## Complexity Tracking

*No Constitution Check violations — this section is not needed.*
