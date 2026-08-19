# Phase 0 Research: Flumeria Studio Workspace Shell

All items below were resolved from the existing codebase (no external unknowns remained after `/speckit-clarify`), so each entry documents a concrete decision grounded in what already exists in `src/AskLucy.Web/ClientApp`, rather than an open technology choice.

## 1. Where do the six reusable components live?

**Decision**: `src/components/workspace-shell/` — a new subfolder inside the existing `src/components/` directory.

**Rationale**: The constitution's §4 "Folder structure" names `src/shared` as the convention for cross-feature primitives, but that directory does not exist anywhere in this codebase. The actual, consistently-followed convention (`AppShell.tsx`, `EmptyState.tsx`, `ErrorState.tsx`, `BrandMark.tsx`, `UserMenu.tsx`, `ConfirmDialog.tsx`, `SkeletonBlock.tsx` — all cross-feature, all used by ≥2 features) is a flat `src/components/` directory. Per constitution §2.VII (Convention Over Configuration), a strong, documented *project* convention — even one that diverges from the constitution's own illustrative example — is followed rather than introducing a second, competing location for the same purpose.

**Alternatives considered**: Creating `src/shared/workspace-shell/` to match the constitution text literally — rejected because it would split "cross-feature shared component" into two parallel homes with no way for a future contributor to know which one to use for what, which is a worse outcome than the one paragraph of documentation this decision requires.

## 2. Expand/collapse motion — what drives it?

**Decision**: MUI's own transition primitives (`Collapse`, `Grow`, `Fade`, or a `sx`-driven `theme.transitions.create([...])` on `width`/`height`/`border-radius`), timed by the existing `theme.transitions` object, which is itself built from `createMotionTokens()` (`src/theme/tokens/motion.ts`).

**Rationale**: `createMotionTokens(prefersReducedMotion)` already collapses all durations to `0` when the user prefers reduced motion, and `theme/index.ts` wires this into `theme.transitions` globally — every existing themed transition (including `AssistantPanel`'s own transform/opacity transition) gets FR-018 (reduced-motion) compliance "for free" by using `theme.transitions.create(...)` instead of a hardcoded duration. Introducing a dedicated animation library (e.g., Framer Motion) would duplicate this mechanism, require its own reduced-motion branching, and add a new dependency for a codebase that presently has none — a direct YAGNI violation (constitution §2.III) given MUI's primitives already satisfy every animation requirement in the spec (smooth, ~300ms, reduced-motion-aware, keyboard-operable).

**Alternatives considered**: Framer Motion or a custom CSS-keyframe engine — rejected for the reasons above; a component-local `prefers-reduced-motion` media query — rejected because it would duplicate logic `createMotionTokens` already centralizes.

## 3. Expand-in-place vs. detached popover

**Decision**: Circular controls expand **in place** — the trigger itself grows from a circle into a pill/rectangle, anchored at its own screen position — rather than opening a detached `Popper`/menu elsewhere on screen.

**Rationale**: This is what the spec's interaction pattern (`CircularAction` → `ExpandableActionGroup` per FR-006/FR-007) and the referenced design direction (readdy.ai Studio preview, WhatsApp-style floating buttons) both describe: the control *becomes* the container, it doesn't summon a separate one. A `ClickAwayListener` still wraps the expanded state to satisfy the "tap outside collapses it" edge case, but positioning is anchored to the trigger's own `Box`, not a `Popper` with its own placement/collision logic.

**Alternatives considered**: `Popper`-based dropdown (like `ProviderModelSelector`'s existing menu pattern) — rejected because it reads as a conventional dropdown menu, which is the exact "toolbar/menu" chrome pattern the spec (FR-004) asks to avoid; the whole point of the circular-to-pill interaction is that the control's own footprint changes, not that a second surface appears next to it.

## 4. State ownership — one store or many?

**Decision**: A single new Zustand store, `workspaceOverlayStore`, owns `expandedControlId: string | null` plus `expand(id)` / `collapse()` / `toggle(id)`, enforcing FR-015 ("at most one expanded control at a time") in one place. It also owns the current `viewMode: '2D' | '3D'` and a small per-control unread/badge map (today only used by the `chat` control, mirroring `assistantPanelStore`'s existing `hasUnreadWhileCollapsed`). The existing `assistantPanelStore.ts` is removed; every place that read/wrote `isOpen`/`toggle`/`markUnread` now reads/writes `workspaceOverlayStore` with `controlId: 'chat'`.

**Rationale**: FR-015 is a cross-control invariant ("only one expanded control, period") — it cannot be correctly enforced by six independent per-component `useState`/stores each guessing at the others' state. A single store is the natural place to own it, and it is the same pattern `assistantPanelStore` already established for one control; this generalizes it to all controls instead of running two competing "what's currently open" sources of truth side by side.

**Alternatives considered**: Keep `assistantPanelStore` for chat and add a separate store for the new tool controls — rejected because two independent stores cannot jointly guarantee "at most one of six controls is expanded" without one watching the other, which is just a worse-encapsulated version of the single store.

**Persistence**: Unlike `assistantPanelStore` (which persists `isOpen` via `zustand/middleware`'s `persist`), `workspaceOverlayStore` does **not** persist — per the spec's Assumptions, expand/collapse state and view mode are session-scoped only. A returning user always lands on Studio with every control collapsed, which is also simpler to reason about than resurrecting "control X was expanded" days later.

## 5. Keyboard & screen-reader pattern

**Decision**: Model each `CircularAction` as a WAI-ARIA **disclosure** widget: `aria-expanded` + `aria-controls` on the trigger button, `Enter`/`Space` toggles it (native `<button>` behavior via MUI `IconButton`/`Fab`), `Escape` collapses and returns focus to the trigger, and a natural DOM tab order through the controls (no roving `tabindex` needed since there are only a handful of top-level triggers, not a large repeating list).

**Rationale**: A disclosure widget is the correct ARIA pattern for "a button that reveals/hides content in place" — it is *not* a menu (no arrow-key roving selection semantics implied) and *not* a modal dialog (it doesn't need a focus trap or `aria-modal`, since the rest of the workspace remains visible and interactive per FR-004's "no permanent chrome" intent — only the chat `FloatingPanel` holds enough content that focus should move into it on open, still without trapping it).

**Alternatives considered**: Full modal focus-trap per expanded control — rejected as overkill for single-icon tool placeholders (layers/navigation/selection/analysis), though the chat panel's richer content still benefits from moving initial focus inside it.

## 6. "Coming soon" placeholder content

**Decision**: A static array of `ControlDefinition` objects (id, icon, label, `status: 'functional' | 'coming-soon'`) drives which controls render real content vs. a simple inline "coming soon" message (MUI `Typography`/`Alert`-style, no dedicated backend flag or feature-flag service).

**Rationale**: FR-021 explicitly scopes real layers/navigation/selection/analysis behavior out of this feature; a static list is the simplest thing that satisfies FR-012 (visible, clearly-labeled, not "indistinguishable from a working control") without inventing a feature-flagging mechanism this feature doesn't need (YAGNI).

## 7. AI Presence Card

**Decision**: `AiPresenceCard` is a small, fixed-size, persistently-rendered floating rounded-square card (not gated behind a `CircularAction`/expand state) that hosts the existing `SceneBackground`/`ReactiveSphere`/`ParticleSphereBloom` three.js scene and `useVoiceOutput` wiring, unchanged, just constrained to the card's bounds instead of the full viewport.

**Rationale**: The clarification answer describes it as "a separate floating rounded square card over the view, same as the example provided in the readdy.ai link" — i.e., a persistent presence indicator (comparable to how `AssistantToggleFab` today always shows Lucy's portrait), not a sixth expandable control. Keeping the existing scene components unchanged (only re-parenting/re-sizing them) avoids re-deriving the particle-sphere engine (spec 011) and its 60fps performance work.

**Alternatives considered**: Folding the sphere into the chat `FloatingPanel`'s header — rejected during clarification in favor of a dedicated card, matching the readdy.ai reference more directly and keeping the AI presence visible even while chat is collapsed.

## 8. Workspace surface visual

**Decision**: `WorkspaceSurface` is a lightweight CSS-only "soft alternating gradient" background (an animated `linear-gradient`/`conic-gradient` via `sx`, or a small styled `Box`), not a canvas/WebGL scene.

**Rationale**: The spec is explicit that this feature does not build real spatial/GIS/BIM content (FR-022) — spinning up a second three.js canvas for a placeholder would add render cost and bundle weight for a surface with no interactive purpose yet, contradicting constitution §15 (frontend performance: large dependencies lazy-loaded only behind the feature that needs them). A CSS gradient is the simplest thing that reads as "reserved for future spatial content" without pretending to be that content.

**Alternatives considered**: Reusing `SceneBackground` at full viewport for the surface too — rejected per Clarification Q1's resolution (surface and AI presence are now explicitly separate concerns).

## 9. Route rename and redirect

**Decision**: `router.tsx` gets a `/studio` route (replacing `/chat`'s entry) rendering the (renamed) workspace page, plus a `/chat` route that renders `<Navigate to="/studio" replace />` for FR-002. Every other hardcoded `/chat` string literal found in the codebase (`AppShell.tsx`'s home link and `isHome` check, `ErrorPage.tsx`, `AdminRoute.tsx`, `PublicOnlyRoute.tsx` + its test, `LoginPage.tsx`, `ExternalLoginCompletePage.tsx`, `LandingCtaBar.tsx` + its test) is updated to `/studio` so internal navigation never round-trips through the redirect unnecessarily.

**Rationale**: `react-router`'s `<Navigate replace>` is the standard client-side redirect mechanism already available (no new dependency), and `replace` (not push) means the redirect doesn't leave a `/chat` entry in browser history, matching SC-005's "land the user at `/studio`" (not "land at `/studio` with `/chat` one back-button-press away").

## 10. `ChatPage.tsx` restructuring shape

**Decision**: `ChatPage.tsx` becomes the `/studio` page's top-level component, composing (in place of today's `SceneBackground` + `MinimalTopBar` + `AssistantPanel` + `AssistantToggleFab`): `WorkspaceSurface` (full-bleed background) → `WorkspaceOverlay` (coordinating layer, hosting a `FloatingToolbar` of view-mode/layers/navigation/selection/analysis `CircularAction`s plus the chat `CircularAction`) → `AiPresenceCard` (persistent, outside the overlay's single-expanded-control rule). `AssistantPanel`'s existing internal content (`ConversationSwitcher` + the full `ConversationView` — composer, message list, voice controls, etc.) becomes the `children` of a `FloatingPanel` opened by the chat control, with no change to its own internals or to `ConversationView`.

**Rationale**: This is the minimal restructuring that satisfies FR-005/FR-013/FR-016 (everything reachable only through circular controls, chat using the same mechanism) while leaving `ConversationView` — the actual chat business logic (streaming, voice, provider selection, etc.) — completely untouched, satisfying FR-014/SC-006 (zero functional regression).

## 11. Account/session access (added post-`/speckit-analyze`)

**Decision**: An `account` `ControlDefinition` (`kind: 'action-group'`, `status: 'functional'`) wraps the existing `UserMenu` component's destinations (Profile, Settings, Documents, Knowledge Bases, Memory Center, Prompts, Agents, Workflows, Admin panel, Privacy Policy, Log out) plus the theme-toggle `IconButton`, both reused unchanged from `MinimalTopBar`/`AppShell`, as `ExpandableActionGroupAction`s inside the same circular-control pattern as every other control.

**Rationale**: `/speckit-analyze` found that `MinimalTopBar` — being removed by US1's restructuring — was the *only* place these were reachable from this route (`AppShell`, which hosts the same functions elsewhere, is never mounted on this page). Without an explicit replacement, users would lose the ability to log out or navigate anywhere else in the product from their primary post-login page. Reusing `UserMenu`/the existing theme-toggle logic unchanged (rather than rebuilding them) keeps this fix minimal and avoids duplicating already-tested account/session logic.

**Alternatives considered**: A persistent (non-circular) mini top bar just for account/theme — rejected as reintroducing exactly the "permanent chrome" this feature exists to remove (FR-004); it also would have been the seventh piece of chrome exempted from FR-005 with no principled reason.
