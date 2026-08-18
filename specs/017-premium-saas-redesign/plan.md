# Implementation Plan: Premium AI SaaS UI/UX Redesign

**Branch**: `017-premium-saas-redesign` | **Date**: 2026-08-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/017-premium-saas-redesign/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Redesign the visual identity and interaction design of every user-facing surface (chat
workspace, navigation/chrome, knowledge base, documents, settings, auth, profile,
privacy/consent) into a single, premium, 2026-grade design language — while preserving
100% of existing functionality, APIs, routing, and state management. The codebase already
has a distinctive, well-considered design-token foundation from SPEC-010 (a "drafting
table" palette, Space Grotesk/Inter/JetBrains Mono type system, elevation scale, and
scoped glassmorphism tokens for the chat assistant panel) plus an established a11y-testing
convention (`*.a11y.test.tsx` with jest-axe). The technical approach is therefore
**completion and generalization**, not creation from scratch: extend the existing token
set with the missing categories (motion timing/easing, opacity, z-index — the existing
spacing scale is retained as-is, see research.md #2), expand the MUI component-override library to the many components it does
not yet cover (Select/Autocomplete, Menu, Tabs, Table, List, Avatar, Badge, Alert,
Snackbar, Skeleton, Progress, Drawer), extract shared AI-activity-state and empty/loading/
error-state primitives (today ad hoc per feature), and unify navigation (today a
per-page `PageHeader` back-link pattern, not a persistent shell) — then apply all of this,
one application surface at a time, per FR-013's verify-before-ship rule.

## Technical Context

**Language/Version**: TypeScript ~6.0 (`strict` mode), React 19.2, targeting the existing
Vite 8 build.

**Primary Dependencies**: MUI v9 (`@mui/material`, `@mui/icons-material`) + Emotion as the
theming/styling engine (per constitution §7 — no replacement framework); `react-router`
v8; `@tanstack/react-query` v5 (server state) and `zustand` v5 (client/UI state, incl.
`themeStore` for light/dark mode); `@tanstack/react-virtual` (long list virtualization);
`react-hook-form` (forms); `react-markdown` + `remark-gfm`/`remark-math` + `rehype-katex`
(chat/message rendering); `@react-three/fiber` + `@react-three/drei` +
`@react-three/postprocessing` + `three` (the existing particle-sphere assistant scene,
preserved per Clarification 2); `@microsoft/signalr` (streaming chat transport).

**Storage**: N/A — this is a presentation-layer-only feature; no schema, API contract, or
persistence changes (FR-012).

**Testing**: `vitest` + `@testing-library/react` + `@testing-library/user-event` for
component/unit tests; `jest-axe` for automated accessibility checks, following the
codebase's existing `ComponentName.a11y.test.tsx` convention (already present for
~10 components/pages); `msw` for API mocking in tests that need server state. No new
test tooling is introduced (see research.md — visual regression tooling was considered
and rejected as unnecessary for this feature's scale).

**Target Platform**: Modern evergreen browsers (Chrome, Edge, Firefox, Safari) across
mobile, tablet, and desktop viewports; the SPA is built by Vite and served from the
existing ASP.NET Core `AskLucy.Web` host.

**Project Type**: Web application — existing frontend SPA (`src/AskLucy.Web/ClientApp`)
+ existing ASP.NET Core backend. This feature touches the frontend only.

**Performance Goals**: Perceived load time (time to first meaningful content) for every
redesigned page MUST be ≥ its pre-redesign baseline (SC-005); the existing per-route
code-splitting (`React.lazy` in `routes/router.tsx`) and bundle-size budget (constitution
§15) MUST be preserved, not regressed, by new shared primitives or the particle-sphere
scene.

**Constraints**: No backend/API/contract changes (FR-012); MUI remains the component/
theming foundation (FR-014); WCAG 2.1 AA on every redesigned surface (FR-004); light and
dark theme parity everywhere (FR-002); `prefers-reduced-motion` MUST be respected
(FR-010); the existing young-adult female voice persona is unaffected (Clarification 2);
admin/internal-only surfaces (Admin Dashboard, AI Provider/Model Catalog, Admin Users)
were originally excluded (Clarification 1) but were **brought into scope** in a follow-up
session (see spec.md's revised Clarifications) after the pre-redesign/redesigned contrast
proved visually jarring in practice.

**Scale/Scope**: ~9 in-scope feature areas across ~18 routed pages (`/chat`, `/documents`,
`/knowledge-bases`, `/knowledge-bases/:id`, `/settings`, `/profile`, `/privacy`, `/login`,
`/register`, `/confirm-email`, `/confirm-email-change`, `/auth/external-complete`,
`/admin/dashboard`, `/admin/users`, `/admin/ai-providers`) plus the shared navigation
chrome and component library consumed by all of them, delivered incrementally per
FR-013/Clarification 3 (each page ships directly to all users as it's verified — no
feature flag).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|---|---|---|
| §3 Clean Architecture / Dependency Rule | **N/A** | Presentation-layer-only change; no Domain/Application/Infrastructure/API code is touched. |
| §3 CQRS / Repository / DI rules | **N/A** | No backend change. |
| §5 Database Principles | **N/A** | No schema change. |
| §6 API Standards | **N/A** | No new/changed endpoints; FR-012 explicitly forbids contract changes. |
| §7 UI Principles — design system | **PASS (goal of this feature)** | Extends the existing MUI theme/tokens rather than replacing them; a new shared component requires ≥2 consumers or a documented foundational-primitive justification, per §7. |
| §7 UI Principles — accessibility | **PASS (verified per page)** | WCAG 2.1 AA + automated a11y checks (jest-axe) required before each page ships (FR-004, FR-013). |
| §7 UI Principles — responsive/theming | **PASS (verified per page)** | Both themes, all breakpoints, verified before each page ships (FR-002, FR-005, FR-013). |
| §7 UI Principles — state management | **PASS** | Zustand (UI state) / TanStack Query (server state) boundary is unchanged (FR-012); no server state is duplicated into Zustand by new work. |
| §7 UI Principles — performance | **PASS (verified per page)** | Existing route-level code splitting and virtualization are preserved, not reduced (Technical Context above). |
| §7 UI Principles — voice output persona | **PASS** | Explicitly unaffected (Clarification 2); no change to voice selection logic. |
| §2.VIII No Silent Failures | **PASS (goal of this feature)** | FR-008 requires an explicit empty/loading/error state everywhere one can occur — this *tightens* compliance versus today's ad hoc states, it does not weaken it. |
| §10 Testing Standards | **PASS** | Existing `*.a11y.test.tsx` convention extended to newly redesigned components/pages; component/hook unit tests updated alongside behavior-preserving visual changes; no integration/E2E tests needed since no API contracts change. |
| §13 Documentation | **PASS** | Spec + plan live under `specs/017-premium-saas-redesign/`; no ADR required — this generalizes an existing, already-adopted pattern (SPEC-010's token system) rather than introducing a new architectural pattern. |
| §15 Performance | **PASS (verified per page)** | Bundle-size budget per route and virtualization for long lists are explicitly preserved (Technical Context above). |

No violations requiring justification — **Complexity Tracking is empty**.

**Post-Phase 1 re-check**: research.md, data-model.md, and contracts/ introduce no new
dependency, no backend/API surface, and no architectural pattern beyond what SPEC-010
already established (a themed design-token system). All gates above remain **PASS/N/A**
unchanged after design — no new Constitution Check entries or violations were introduced
by Phase 1.

## Project Structure

### Documentation (this feature)

```text
specs/017-premium-saas-redesign/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── design-tokens.md
│   └── component-library.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/
├── src/
│   ├── theme/
│   │   ├── index.ts                 # createAppTheme(mode) — extended, not replaced
│   │   └── tokens/
│   │       ├── palette.ts           # existing — color + radius (extend as needed)
│   │       ├── typography.ts        # existing — type scale (extend as needed)
│   │       ├── shadows.ts           # existing — elevation scale (extend as needed)
│   │       ├── glass.ts             # existing — glassmorphism (generalize scope)
│   │       ├── components.ts        # existing — MUI overrides (expand coverage)
│   │       ├── motion.ts            # NEW — timing/easing tokens (Phase 1)
│   │       └── zIndex.ts            # NEW — z-index hierarchy (Phase 1)
│   ├── components/                  # cross-feature shared primitives — flat files,
│   │   │                            # matching existing convention (not folder+barrel)
│   │   ├── PageHeader.tsx           # REMOVED in Phase 8 — zero consumers left after Admin migrated
│   │   ├── ErrorPage.tsx            # existing
│   │   ├── AppShell.tsx             # NEW — shared nav shell primitive (research.md decision)
│   │   ├── EmptyState.tsx           # NEW — shared empty-state primitive
│   │   ├── ErrorState.tsx           # NEW — shared inline error-state primitive
│   │   ├── SkeletonBlock.tsx        # NEW — shared skeleton-loader primitive
│   │   └── AiActivityIndicator.tsx  # NEW — generalized from chat's ThinkingIndicator
│   ├── hooks/
│   │   └── usePrefersReducedMotion.ts  # NEW — shared reduced-motion hook (research.md)
│   └── features/
│       ├── chat/                    # P1 — pages/components restyled in place
│       ├── knowledge-base/          # P3
│       ├── documents/               # P3
│       ├── settings/                # P3
│       ├── profile/                 # P3
│       ├── auth/                    # P3
│       ├── privacy/                 # P3
│       └── consent/                 # P3
└── tests/                           # co-located *.test.tsx / *.a11y.test.tsx (existing convention)
```

**Structure Decision**: No new projects, packages, or backend changes. All work lands
inside the existing `src/AskLucy.Web/ClientApp/src` tree, following the codebase's
established `src/theme` (tokens) + `src/components` (cross-feature shared primitives) +
`src/features/<domain>` (feature-scoped UI) layout (constitution §4 Folder Structure).
New shared primitives are added under `src/components/` and `src/hooks/` only where a
capability is genuinely cross-feature (nav shell, empty/error/loading states, AI activity
indicator, reduced-motion hook) — everything else is a restyle of existing feature
components in place, not a rewrite.

## Complexity Tracking

*No Constitution Check violations — this section is intentionally empty.*

## Phase 8: Admin surfaces (added after initial delivery)

Admin Dashboard, Admin Users, and AI Provider/Model Catalog management were originally
excluded (Clarification 1) and shipped Phases 1–7 still on the pre-redesign `PageHeader`
component. After review, that exclusion was reversed (spec.md's revised Clarifications) —
the visual contrast between redesigned and un-redesigned pages was worse than deferring
the whole thing would have been. This phase brings all three onto `AppShell`/the shared
component library, matching every other page, and retires `PageHeader.tsx` entirely once
they're the last consumers migrated (see tasks.md T041/T070–T075).
