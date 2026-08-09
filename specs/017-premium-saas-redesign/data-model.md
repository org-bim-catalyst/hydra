# Phase 1 Data Model: Premium AI SaaS UI/UX Redesign

This feature is presentation-layer-only (FR-012) — there is no database schema, no new
persisted entity, and no API contract change. The "entities" below are the frontend
design/UI concepts identified in the spec's Key Entities section, expressed as the
concrete TypeScript shapes Phase 1 design settled on. No entity here is persisted to the
backend; several map to existing `zustand` stores or are pure, stateless configuration.

## Design Token Set

Not a runtime data structure a user acts on — a compile-time/theme-time configuration
object assembled by `theme/index.ts#createAppTheme(mode)`. Extended per research.md #2/#3.

| Module | Shape | Status |
|---|---|---|
| `palette.ts` | `createPalette(mode): PaletteOptions`, plus `radius` scale | Existing — extend with a named opacity scale (`opacity.disabled`, `opacity.hover`, `opacity.overlay`) |
| `typography.ts` | `typography: TypographyVariantsOptions` | Existing — no gap identified |
| `shadows.ts` | `createShadows(isDark): Shadows` (25-step elevation) | Existing — no gap identified |
| `glass.ts` | `createGlassTokens(mode): GlassTokens` | Existing — generalize consumer scope (research.md #3), shape unchanged |
| `components.ts` | `createComponents(): Components<Theme>` | Existing — extended incrementally per page (research.md #8) |
| `motion.ts` **(new)** | `{ duration: { fast, standard, slow }, easing: { standard, decelerate, accelerate } }` plus a `reduced` variant of `duration` | New — research.md #4 |
| `zIndex.ts` **(new)** | Named layer constants (e.g., `appShell`, `dropdown`, `dialog`, `snackbar`, `tooltip`) mapped onto MUI's `zIndex` theme key | New — FR-006 |

Validation rule: every new token value must be consumed through the MUI theme (via
`sx`/`styled`/theme-aware component props) — constitution §7 forbids components
hardcoding colors/spacing/timing that bypass the theme.

## Application Surface

Represents one independently-redesignable, independently-verifiable unit of work
(FR-013). Not a runtime object — a planning/tracking concept carried into `tasks.md`.

| Field | Description |
|---|---|
| `name` | Human-readable surface name (e.g., "Chat Workspace", "Navigation Shell") |
| `priority` | P1–P4, matching the spec's User Stories |
| `routes` | The `react-router` paths this surface owns (e.g., `/chat`) |
| `verificationChecklist` | Functional parity, both themes, responsive breakpoints, WCAG 2.1 AA (jest-axe), reduced-motion — all four required before `verified = true` (FR-013) |
| `verified` | boolean — gates that surface's page from being considered shipped |

Relationships: an Application Surface consumes the Design Token Set and zero or more
Component Patterns; it does not own or duplicate them.

## Component Pattern

A reusable UI primitive. Each new one below was justified in research.md by an existing
≥2-consumer duplication (constitution §7), not spec-level speculation.

| Component | Props (shape) | Replaces / generalizes |
|---|---|---|
| `AppShell` | `{ children: ReactNode }` — renders persistent top bar + nav; reads active route from `react-router` | `PageHeader`'s back-link pattern (research.md #1) |
| `EmptyState` | `{ icon?: ReactNode; title: string; description?: string; action?: ReactNode }` | Ad hoc empty messaging in `documents`/`knowledge-base` features |
| `ErrorState` | `{ title: string; description?: string; onRetry?: () => void }` | Ad hoc inline error messaging |
| `SkeletonBlock` | `{ variant: 'text' \| 'card' \| 'row'; count?: number }` | Ad hoc `Skeleton` usage per feature |
| `AiActivityIndicator` | `{ state: 'thinking' \| 'streaming' \| 'tool-executing'; label?: string }` | `features/chat/components/ThinkingIndicator.tsx` + `features/documents/components/ProcessingStatusBadge.tsx` (research.md #5) |

Validation rule: a new shared component must be consumed by ≥2 features/pages (constitution
§7) or documented here as a foundational primitive — all five above satisfy the ≥2-consumer
bar per research.md.

## AI Activity State

The state a given `AiActivityIndicator` instance renders, derived entirely from data the
API already exposes (streaming status via SignalR, tool-execution status from existing
document-processing/agent responses) — no new backend field is introduced.

| Value | Meaning | Existing source |
|---|---|---|
| `thinking` | Model has not yet started streaming tokens | Existing chat stream state (`useChatStream`) |
| `streaming` | Tokens are actively arriving | Existing chat stream state |
| `tool-executing` | A tool/agent action is in progress | Existing document-processing status (`ProcessingStatusBadge`'s current status enum) |

## User Preference

Not new — an existing, already-persisted concept (`themeStore`'s light/dark mode,
settings-feature language/provider/model choices). This feature's only obligation (FR-011)
is that redesigned surfaces continue to read/write the same existing store keys and API
fields; no new preference field is introduced.
