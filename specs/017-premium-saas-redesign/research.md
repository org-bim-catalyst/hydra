# Phase 0 Research: Premium AI SaaS UI/UX Redesign

Each unknown below was resolved by auditing the existing `src/AskLucy.Web/ClientApp`
codebase directly (constitution §7 requires reusing the existing design system before
adding new pieces), rather than by speculative best-practice research — the answer to
"what's missing" is determined by what's already there.

## 1. Navigation shell: build a persistent shell, or unify the existing back-link pattern?

- **Decision**: Introduce a shared `AppShell` primitive (persistent top bar + collapsible
  navigation, used by every authenticated page) that *replaces* the current per-page
  `PageHeader` "back to chat" pattern, rather than merely restyling `PageHeader` in place.
- **Rationale**: Auditing `src/components/PageHeader.tsx` and `routes/router.tsx` shows
  today's IA is "Chat is home; every other page is a one-level-deep sub-page reached via
  the user menu, navigated back from via an arrow icon." That pattern is exactly the kind
  of inconsistency User Story 2 exists to remove — reference products (Notion, Linear,
  ChatGPT) all use a persistent nav surface, and the spec explicitly calls out "Sidebars,"
  "Top navigation," and "Workspace organization" as redesign targets, not just restyle
  targets. A persistent shell is also what makes cross-page consistency (FR-001, SC-007)
  achievable — restyling seven different back-links can't produce "one coherent app."
- **Alternatives considered**: (a) Keep `PageHeader`'s back-link IA and only restyle it —
  rejected because it does not resolve the underlying inconsistency the P2 user story
  describes, only its appearance; (b) build a full persistent *sidebar* like Notion/Linear
  — deferred: the current IA has no sidebar at all outside chat's own `ChatSidebar`
  (conversation list), and introducing one is a bigger IA change than "redesign"; a top
  bar + contextual navigation menu achieves the same consistency with a smaller,
  reviewable diff per FR-013, and is documented here as the starting shape — the exact
  shell composition is a page-by-page implementation decision, not a spec-level constraint.

## 2. Missing design token categories

- **Decision**: Add three new token modules alongside the existing four
  (`palette.ts`, `typography.ts`, `shadows.ts`, `glass.ts`): `motion.ts` (timing durations
  + easing curves), `zIndex.ts` (documented layering hierarchy), and an opacity scale
  (folded into `palette.ts` as named constants rather than a new file, since it is a small,
  closely-related addition). A documented spacing scale is **not** a new token file — MUI's
  default 8px spacing unit is already in use throughout and is retained as-is (YAGNI: no
  evidence of inconsistency to justify replacing it).
- **Rationale**: `FR-006` requires centralized tokens for "motion timing," "z-index
  hierarchy," and "opacity," and none of the three exist today (confirmed by reading all
  five files under `theme/tokens/`). Everything else FR-006 asks for (color, typography,
  spacing, radius, shadow/elevation) is already centralized and does not need to be
  rebuilt — only extended where a specific gap is found during a page's audit.
- **Alternatives considered**: A single monolithic `tokens.ts` re-export — rejected in
  favor of keeping the existing one-concern-per-file convention already established by
  the four existing token files.

## 3. Glassmorphism scope

- **Decision**: Generalize `createGlassTokens` (currently documented as scoped to "the
  floating assistant panel") into a token consumed by the new `AppShell` top bar and by
  dialogs/drawers that float over page content, but explicitly **not** applied to dense,
  data-heavy surfaces (settings forms, document/knowledge-base tables) — those stay
  opaque `Paper`/`Card` surfaces per the existing `MuiPaper`/`MuiCard` overrides.
- **Rationale**: The user's brief explicitly warns "avoid excessive decoration... minimalism
  should increase clarity, not reduce usability," and `glass.ts`'s own inline comment
  explains the effect exists so a floating surface "reads as part of this theme rather
  than a bolted-on effect" over the animated 3D scene — that reasoning only applies to
  surfaces that float over motion/imagery, not to information-dense forms and tables where
  translucency would hurt legibility and contrast (risking the WCAG 2.1 AA gate, FR-004).
- **Alternatives considered**: Applying glass everywhere for visual signature consistency —
  rejected as directly contradicting the "avoid excessive decoration" instruction and the
  accessibility gate for text-heavy surfaces.

## 4. Reduced-motion strategy

- **Decision**: Add one shared `usePrefersReducedMotion()` hook (wrapping the
  `prefers-reduced-motion` media query) plus a `motion.ts` token pair (`standard` /
  `reduced` duration sets). Components consume the hook and select the token set; MUI's
  `Transitions` `duration`/`easing` config in `theme/index.ts` is set from the same source
  so default MUI transitions (`Dialog`, `Drawer`, `Menu`, `Collapse`) inherit it for free.
- **Rationale**: FR-010 and Edge Cases both require respecting this OS-level preference
  platform-wide; centralizing it in one hook + one theme setting (rather than per-
  component `matchMedia` checks) satisfies constitution §7 DRY guidance and makes it
  impossible for a new page to forget the behavior.
- **Alternatives considered**: A global CSS-only approach (`@media (prefers-reduced-motion)`
  wrapping all `transition`/`animation` rules) — rejected as insufficient on its own
  because the `@react-three/fiber` particle-sphere scene's motion is driven by JS
  (`requestAnimationFrame`), which CSS media queries cannot gate; the hook is required for
  that surface regardless, so it is used uniformly rather than mixing two mechanisms.

## 5. AI activity-state indicator consolidation

- **Decision**: Extract a shared `AiActivityIndicator` primitive (thinking / streaming /
  tool-execution-in-progress states) generalized from the existing
  `features/chat/components/ThinkingIndicator.tsx`, placed under `src/components/` so
  non-chat surfaces (e.g., a future document-processing or agent-status view) can reuse
  the same visual language for "the AI is doing something" (FR-007, User Story 4).
- **Rationale**: `ThinkingIndicator` already exists and is already tested
  (`ThinkingIndicator.test.tsx`); constitution §7 requires reusing/extending an existing
  component before writing a new one, and requires a new shared component to be justified
  by ≥2 consumers — `ProcessingStatusBadge.tsx` (documents feature) is a second, currently
  independent implementation of the same concept, confirming the ≥2-consumer bar is met.
- **Alternatives considered**: Leaving `ThinkingIndicator` chat-scoped and building a
  separate primitive for documents — rejected as the exact duplication constitution §7
  and §2.III (DRY) argue against.

## 6. Empty / loading / error state primitives

- **Decision**: Add three small shared primitives — `EmptyState`, `ErrorState`, and
  `SkeletonBlock` — under `src/components/`, each accepting an icon/illustration slot,
  title, description, and optional action, matching the existing `ErrorPage.tsx` visual
  language at a smaller (in-panel, not full-page) scale.
- **Rationale**: FR-008 requires one of these three states everywhere a page/list/panel
  can be empty, loading, or failed; auditing `features/documents` and
  `features/knowledge-base` shows several already-implemented, feature-local variants
  (e.g., `DocumentFilterBar`'s empty result messaging) that differ from each other in
  spacing and tone — exactly the "duplicated styling" the spec's Refactoring Guidelines
  call out for consolidation.
- **Alternatives considered**: Standardizing on MUI's bare `Alert`/`Skeleton` primitives
  inline per usage — rejected because it reproduces the current inconsistency rather than
  fixing it; a shared component is the smallest change that satisfies FR-008 uniformly.

## 7. Testing strategy — visual regression tooling

- **Decision**: Do **not** introduce visual regression tooling (e.g., Playwright/Chromatic
  screenshot diffing). Continue with the existing `vitest` + `@testing-library/react` +
  `jest-axe` (`*.a11y.test.tsx`) convention, and treat SC-007 ("no inconsistency... confirmed
  by design sign-off") as a manual review gate performed once per page before it ships
  (already required by FR-013), not an automated one.
- **Rationale**: No visual regression tool exists in `package.json` today; adding one is
  new CI infrastructure outside this feature's scope (constitution §2.III YAGNI — no
  stated requirement for automated visual diffing, and the existing manual-review-per-PR
  process already gates every change). Introducing new CI/CD infrastructure would also
  need to flow through constitution §12, which is out of scope for a presentation-layer
  feature.
- **Alternatives considered**: Adding Playwright + screenshot snapshots — rejected as
  disproportionate new tooling for a spec that explicitly says "do not introduce
  unnecessary architectural changes."

## 8. Component-library coverage gap

- **Decision**: Expand `theme/tokens/components.ts`'s MUI style-override coverage as each
  page's audit surfaces a need, rather than pre-building overrides for every MUI component
  listed in the original brief up front.
- **Rationale**: `components.ts` today only overrides `MuiCssBaseline`, `MuiButton`,
  `MuiPaper`, `MuiCard`, `MuiOutlinedInput`, `MuiAppBar`, `MuiChip`, `MuiDialog`, and
  `MuiTooltip` — everything else (Select, Autocomplete, Checkbox, Radio, Switch, Menu,
  Tabs, Table, List, Avatar, Badge, Alert, Snackbar, Skeleton, Progress, Drawer,
  Breadcrumbs) uses MUI's un-themed defaults. Building overrides for components no
  in-scope page actually uses yet would be exactly the "hypothetical future requirement"
  constitution §2.III (YAGNI) forbids; instead, each page's audit step (workflow already
  specified by the user: audit → propose → implement) adds the override(s) it actually
  needs, in the same page's PR.
- **Alternatives considered**: A big-bang token/override pass across all components before
  touching any page — rejected as violating both YAGNI and FR-013's page-by-page,
  independently-verifiable delivery requirement.

**Output**: All Technical Context unknowns are resolved above; no `NEEDS CLARIFICATION`
markers remain for Phase 1.
