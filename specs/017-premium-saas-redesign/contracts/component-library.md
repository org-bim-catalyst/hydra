# Contract: Shared Component Library Additions

Prop contracts for the five new shared primitives identified in data-model.md, each
justified in research.md by an existing ≥2-consumer duplication. These are the contracts
downstream feature pages (`tasks.md`) code against — changing a prop shape here after a
page adopts it is a breaking change to that page.

## `AppShell`

```ts
interface AppShellProps {
  children: ReactNode
  title?: string
  subtitle?: string
  actions?: ReactNode
}
```

**Implementation-time refinement** (superseding the Phase 1 `{ children }`-only shape):
absorbs `PageHeader`'s title/subtitle/actions job directly, rather than leaving pages to
stack a second chrome layer below the shell — one sticky, glass-backed bar (brand mark as
the home link + theme toggle + account menu, always present) with an optional page
title/actions row beneath it, not glass (research.md #3 — glass is for surfaces floating
over motion/imagery or transient overlays, not dense content). `PageHeader`'s `backTo`/
`backLabel` props have no equivalent here: with a persistent, always-reachable home link
and the account menu carrying the full destination list, an explicit "back" affordance is
redundant. Reads the active route from `react-router` internally (no `activeRoute` prop)
— used to mark the home link `aria-current="page"` when already on `/chat`. Wraps every
authenticated page; replaces `PageHeader` entirely as each page migrates (FR-013 —
migration happens one page at a time, so `AppShell` and `PageHeader` coexist until the
last consuming page migrates, per T041).

## `EmptyState`

```ts
interface EmptyStateProps {
  icon?: ReactNode
  title: string
  description?: string
  action?: ReactNode // e.g. a <Button> to create the first item
}
```

## `ErrorState`

```ts
interface ErrorStateProps {
  title: string
  description?: string
  onRetry?: () => void
}
```

Distinct from the existing full-page `ErrorPage` (router `errorElement`) — `ErrorState`
is an in-panel/in-list failure (e.g., "this document failed to process"), never a full
route-level crash boundary.

## `SkeletonBlock`

```ts
interface SkeletonBlockProps {
  variant: 'text' | 'card' | 'row'
  count?: number // default 1 — renders N repeated skeleton instances
}
```

## `AiActivityIndicator`

```ts
interface AiActivityIndicatorProps {
  state: 'thinking' | 'streaming' | 'tool-executing'
  label?: string // optional override, e.g. "Analyzing document…"
}
```

Generalized from `ThinkingIndicator` (chat) and `ProcessingStatusBadge` (documents) —
both existing components are migrated to render `AiActivityIndicator` internally rather
than duplicating its visual treatment (research.md #5); their existing, more specific
prop contracts (chat-stream-shaped, document-status-shaped) are unchanged for their own
callers.

## Accessibility contract (applies to all five)

Every component above MUST: expose an accessible name (via visible text, `aria-label`, or
`aria-labelledby`), be reachable and operable via keyboard alone, and carry a visible focus
indicator on any interactive element it renders (constitution §7, FR-004) — verified by
that component's `*.a11y.test.tsx` test using `jest-axe`, per the codebase's existing
convention.
