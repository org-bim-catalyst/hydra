# Research: Composer & Panel Layout Refinements

## Decision 1 — Composer restructure: row-flex `Paper` → column-flex `Paper` with a fixed footer `Stack`

**Decision**: Change `ChatComposer.tsx`'s outer `Paper` from `display: 'flex', alignItems: 'center'`
(a single horizontal row holding everything, `borderRadius: radius.pill`) to
`display: 'flex', flexDirection: 'column'`, `borderRadius: radius.lg` (the same rounded-rectangle
token `ExpandedChatPanel.tsx` already uses for its own outer container, keeping the two roundings
visually consistent). Inside: a top `Box` wrapping the `TextField` alone, and a bottom
`Stack direction="row"` (the "footer row") holding every control button in their existing left-to-
right order (attach → insert-prompt → mic/recording-review → mode-switch → preferences-warning →
mute → translate → send). Only the container axis and grouping change — no button is added,
removed, or reordered relative to its neighbors, satisfying FR-014 (no behavior change).

**Rationale**: This is a pure flex-direction restructure of the same children already present in
the component; MUI's `Stack`/`Box`/`Paper` primitives already in use support this without a new
dependency, matching constitution §7's "compose from the existing MUI theme" rule.

**Alternatives considered**: A CSS grid layout (text row spanning full width, button row below) was
considered but rejected — `Stack`/`Box` flex composition is the existing idiom throughout this
codebase (no `display: grid` usage found in the chat feature directory), and flex is sufficient for
a two-row layout with no need for grid's 2D alignment.

## Decision 2 — Capping textarea growth at 6 lines with internal scroll

**Decision**: Keep the existing `TextField` `multiline maxRows={6}` (already present from
specs/029-fix-chat-widget-bugs) as the primary growth cap, and additionally set an explicit
`sx` `lineHeight` on the input slot so the 6-row calculation is based on a known, fixed value
rather than inheriting an ambient value that could vary by container. This makes the cap
deterministic and testable (a component test can assert the computed `max-height`/`line-height`
relationship) rather than relying solely on MUI's internal autosize measurement, which is the
right defensive move given the reported production symptom (composer observed growing to ~11
lines, well past the already-configured `maxRows={6}`).

**Rationale**: FR-004 requires the cap to hold in all cases, not just "usually" — a spec-level
requirement should not depend entirely on a library internal that isn't directly assertable.
Pinning `lineHeight` removes the one variable (ambient/inherited line-height inside a flex
container) most likely to explain the previously observed overshoot.

**Alternatives considered**: Recomputing height in a `ResizeObserver` and manually capping via
inline `style.maxHeight` was considered but rejected as unnecessary complexity (YAGNI, constitution
§3) — the built-in `maxRows` mechanism plus a fixed `lineHeight` is the simplest fix that satisfies
the requirement and stays within the existing MUI `TextField` API surface. This will be verified
empirically against the running app (not just unit-tested) before considering the requirement
satisfied, since autosize behavior is a real-DOM concern jsdom cannot fully validate.

## Decision 3 — "Full window height" while anchored bottom-right via `position: absolute`

**Decision**: `ExpandedChatPanel`'s full-height state sets `height` to
`calc(100vh - 2 * <bottom offset>)` using the *same* offset values `ChatAssistantWidget.tsx`
already uses to anchor the widget (`{ xs: 16, sm: 24 }`), i.e.
`height: { xs: 'calc(100vh - 32px)', sm: 'calc(100vh - 48px)' }`, rather than a literal `100vh`.

**Rationale**: `ChatAssistantWidget.tsx` positions its child with `position: 'absolute', bottom:
{16|24}, right: {16|24}` — the panel grows *upward* from a bottom anchor. A literal `100vh` height
would push the panel's top edge above the viewport by exactly the bottom offset (16–24px),
clipping or forcing an unwanted scrollbar depending on the nearest positioned ancestor's overflow
behavior. Subtracting `2×` the offset keeps an equal, intentional margin at both the top and
bottom of the viewport — genuinely "full window height" in spirit without clipping.

**Alternatives considered**: Switching the anchor to `position: fixed` with `top: 0` when
full-height was considered (simpler math) but rejected — it would change the widget's positioning
model conditionally, which is a bigger, riskier change to `ChatAssistantWidget.tsx` (shared by both
the Collapsed and Expanded states) for a benefit (`0` vs `16–24px` of margin) not requested by the
spec. `100dvh` (dynamic viewport height, more correct on mobile browsers with collapsing address
bars) was also considered but rejected to stay consistent with this file's existing `vh`-based
sizing (`{ xs: 'min(70vh, 600px)' }`) rather than introducing a second viewport-height unit for one
new state — acceptable per the spec's Assumptions, which scope "full window height" to the browser
viewport, not pixel-perfect precision on every mobile browser chrome state.

## Decision 4 — Persisting the half-height/full-height preference

**Decision**: A new, minimal Zustand store `chatPanelSizeStore.ts` using `zustand/middleware`'s
`persist` (localStorage key `ask-lucy-chat-panel-size`), holding `{ isFullHeight: boolean, toggle:
() => void }`, directly mirroring `src/AskLucy.Web/ClientApp/src/store/themeStore.ts`'s existing
`{ mode, toggle }` shape — the closest existing precedent for a simple, local-only, persisted
boolean UI preference.

**Rationale**: Per spec.md's Clarifications (2026-08-20) the user chose "persist as a preference."
Per spec.md's Assumptions, this preference is scoped as a lightweight client-only setting, not a
new backend/Long-Term-Memory-Engine entity — `themeStore.ts`'s pattern is the right-sized existing
precedent (constitution §7 "Convention over Configuration": follow the established local-preference
pattern rather than inventing a heavier one).

**Alternatives considered**: `panelPreferencesStore.ts` (viewer opacity) was considered as a
model since it's also a persisted panel preference, but rejected — that store syncs to a backend
API (`getPanelPreferences`/`savePanelPreferences`) because opacity is a cross-device Settings-page
preference; a chat panel's half/full height toggle has no such requirement per the spec's scoping,
so the added backend round-trip, error state, and Settings UI surface would be unjustified
complexity (YAGNI, constitution §3).

## Decision 5 — Resize/toggle button icon and placement

**Decision**: A single `IconButton` in `ExpandedChatPanel.tsx`'s header `Stack`, inserted
immediately after the existing `onNewChat` "+" `IconButton` (before the `headerTrailing` slot),
showing `RiExpandVerticalLine` (`@remixicon/react`) when at half-height (affordance: "make this
taller") and `RiCollapseVerticalLine` when at full-height (affordance: "make this shorter") — the
same icon-swaps-by-state pattern the mode-switch button in `ChatComposer.tsx` already uses.

**Rationale**: Both icons exist in the already-installed `@remixicon/react` package (verified) and
are semantically about vertical sizing specifically, matching this feature's height-only scope
(spec.md Assumptions: width is unaffected). Reusing the existing icon-swap pattern keeps the
codebase's interaction idiom consistent rather than introducing a new one.

## Decision 6 — Closing the tooltip coverage gap

**Decision**: `ChatComposer.tsx` already wraps its mode-switch, mute, and translate buttons in MUI
`Tooltip`. The remaining icon-only buttons that currently have only an `aria-label` (no visible
`Tooltip`) — attach, insert-prompt, the mic button, and send in `ChatComposer.tsx`; collapse and
new-chat in `ExpandedChatPanel.tsx`, plus the new resize/toggle button from Decision 5 — get a
`Tooltip` added, with `title` text reusing each button's existing `aria-label` string verbatim
(already accurate, already accessible-name-quality copy) rather than authoring new copy. The mic
button's tooltip reuses its existing dynamic `aria-label` (`'Stop voice input'` / `'Start voice
input'`), so it already satisfies FR-012's "reflects current contextual function" requirement
without new logic. The send button (conditionally `disabled`) follows the same
`<Tooltip><span><IconButton disabled /></span></Tooltip>` wrapper pattern the mode-switch button
already uses, since MUI `Tooltip` cannot attach directly to a `disabled` element.

**Rationale**: Reusing existing `aria-label` strings as `Tooltip` titles is the simplest change
that satisfies FR-010/FR-011/FR-013 (accessible, hover- and focus-discoverable) without a new
content/copy system, per spec.md's Assumptions.

**Alternatives considered**: A shared `IconButtonWithTooltip` wrapper component was considered to
avoid repeating the `<Tooltip><IconButton /></Tooltip>` pairing, but rejected — constitution §7's
"a new shared component requires it be used by at least two features" bar is arguably met, but the
existing codebase already has this exact pairing repeated inline at every call site in both files
(no prior wrapper), so introducing one now would be a scope-creeping refactor of already-working,
unrelated code rather than the minimal fix this feature calls for (KISS/YAGNI, constitution §3).
