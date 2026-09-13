# Phase 0 Research: Clear-Area Panel Placement & Reopen Tray

All decisions below were reached against the *current* code, read during planning — not from memory of how the panel system used to work.

---

## D1 — Build the arrangement engine in-house rather than adopting a window-manager library

**Decision**: No new runtime dependency. Implement grid/cascade placement as a pure module under `viewer/panels/layout/`.

**Rationale**: The spec's Assumptions section already carries the full library evaluation (Dockview, react-grid-layout, golden-layout, rc-dock, WinBox.js) performed at the user's explicit request. The decisive point, confirmed by reading the code: **every candidate would still need the app-specific part built by hand.** The hard requirement here (FR-001) is avoiding chrome that lives in a *different React tree* — `WorkspaceOverlay` renders at `zIndex:2` from the page shell, while `FloatingPanelHost` renders at `zIndex:1` inside `ViewerSurface` via `ExtensionOverlayHost` → `PanelsExtensionOverlay`. No generic layout library models "avoid this DOM element I do not own." A library would supply only the packing math — perhaps 80 lines — while costing a rewrite of the panel-type registry, zod validation, context-association, and minimize/restore behavior that specs/049 and specs/050 already ship and test.

**Alternatives considered**: Dockview (closest fit, but a docking/tabs framework that owns its container's DOM and layout state — wrong shape for floating panels over a live 3D canvas); react-grid-layout (good packing reference, but tiles fill a grid container and it has no cascade mode or external-obstacle concept); WinBox.js (closest *concept* — Windows-style cascade + minimize — but vanilla imperative DOM, and its React wrapper `react-winbox` has had no release in ~3 years, so adopting it means abandoning the declarative Zustand model).

---

## D2 — Collect reserved regions by querying the DOM, not by registering them in a store

**Decision**: Chrome that must not be covered carries a `data-panel-reserved` attribute. At each placement event, `collectReservedRects(hostRect)` runs `document.querySelectorAll('[data-panel-reserved]')`, calls `getBoundingClientRect()` on each, and normalizes the results into `FloatingPanelHost`-relative coordinates by subtracting the host rect's origin.

**Rationale**: Three properties made this win over a registration store:

1. **It crosses React trees for free.** `WorkspaceOverlay` is not an ancestor or descendant of `FloatingPanelHost`; a Zustand store would work too, but only if every chrome component imports a panels-owned hook — coupling the page shell and the solar feature to the panel subsystem.
2. **It cannot go stale.** Rects are read at the instant they matter. A store + `ResizeObserver` approach has a window between an observation and the next placement where the store is wrong, and adds unregister-on-unmount lifecycle that can leak a phantom obstacle if an extension unmounts mid-flight.
3. **It is smaller.** ~20 lines and one attribute per chrome element, versus a store, a hook, an observer, and a cleanup path in every consumer.

The cost — placement touches the DOM — is contained by splitting the module in two: `reservedRegions.ts` (the only DOM-aware code, ~20 lines) and `arrangement.ts` (pure functions over plain `Rect` data). All the interesting logic lives in the pure half and is exhaustively unit-testable, which matters because **jsdom returns all-zero rects from `getBoundingClientRect()`** — a design that computed layout directly from DOM measurements would be effectively untestable in this repo's Vitest/jsdom setup.

**Alternatives considered**: A `reservedRegionStore` + `useReservedRegion(ref)` hook with `ResizeObserver` (more "architectural," but more code, a staleness window, a lifecycle-leak failure mode, and needs a `ResizeObserver` stub in `setupTests.ts`); hardcoded rect constants (what the current stopgap in `ExtensionToolbar.tsx` does — already proven fragile, and this feature exists partly to delete it).

---

## D3 — One algorithm: place-into-best-slot, with grid as the zero-overlap outcome

**Decision**: `computeArrangement()` sorts panels **largest-area first**, then places each into the first slot with zero overlap against reserved rects and already-placed panels, scanning a shelf-packed candidate grid within the work area. If every panel lands with zero overlap → the result *is* a grid (FR-005a). If any panel cannot → the whole arrangement is recomputed as a cascade (FR-005b).

**Rationale**: FR-005a (grid), FR-005b (cascade), FR-004 (least-overlap when nothing is clear) and FR-005f (drag-time landing slot) are four requirements that a naive design would satisfy with four code paths. Shelf-packing in decreasing size order is the classic first-fit-decreasing bin packing that naturally *produces* a grid when things fit — so "grid mode" needs no separate implementation, it's just the successful outcome of the general placement pass. Largest-first matters: placing the big panels while space is plentiful is what makes the tight cases fit at all, and it pairs with D4's z-ordering so the large panels are also the ones pushed to the back.

**Alternatives considered**: Computing the largest free rectangle and dividing it into equal cells (produces prettier grids, but wastes space badly when panels differ in size, which they do — `defaultSize` ranges from a 96×96 circular widget to a 400×300 titled box); scanning every pixel position and scoring by overlap area (correct but O(pixels × panels), and produces visually arbitrary near-miss placements rather than aligned rows).

---

## D4 — Cascade z-order is assigned by area, smallest in front

**Decision**: In cascade mode, panels are sorted by area descending and assigned increasing `zOrder`, so the smallest panel ends up frontmost (FR-005c). Focusing a panel still raises it above all others — user intent beats the automatic rule.

**Rationale**: The failure this prevents is concrete: the solar feature contributes a 96×96 camera-attitude widget alongside a 400×300 figures panel. Under the current "newest on top" rule, the big panel can completely swallow the small one, leaving nothing to click to recover it. Area-ordering guarantees that whatever is underneath is always the larger target, so some part of it stays reachable. Keeping `focusPanel` authoritative avoids the obvious trap of an automatic rule that fights the user when they deliberately click the large panel.

**Alternatives considered**: Strict newest-on-top (the status quo — exactly the reported bug); letting each panel declare a z-priority in its `PanelChrome` (pushes a layout concern into every panel-type definition, and no caller has information the area doesn't already give).

---

## D5 — A panel the user has moved is pinned; explicit "arrange" is the one thing that overrides it

**Decision**: Add `manuallyPlaced: boolean` to `FloatingPanel`. The existing `updatePosition`/`updateSize` actions (called only from user gestures — `react-rnd`'s `onDrag`/`onDragStop`/`onResizeStop` and the keyboard nudge handler) set it to `true`. A new `applyArrangement(positions)` action writes positions *without* setting it. `computeArrangement` treats manually-placed panels as **reserved obstacles**, not as panels to place. The explicit arrange action (FR-005e) clears every flag first, so it genuinely re-flows everything.

**Rationale**: The store already has exactly the right seam — `updatePosition` is the user-gesture path and is called from nowhere else, so the flag needs no new plumbing to be accurate. Treating a pinned panel as an obstacle rather than skipping it is the subtle part: it means later panels flow *around* the user's chosen spot instead of being placed on top of it, which is what FR-005d actually implies. Note `clampToViewport` deliberately does **not** set the flag — it nudges a panel back on-screen without claiming the user chose that spot.

**Alternatives considered**: A timestamp instead of a boolean (no requirement needs "how long ago," and it invites an arbitrary expiry heuristic); inferring "manual" by comparing against the last arranged position (fragile — equality on floats, and a user drag that lands back on the computed spot would be misread as automatic).

---

## D6 — The drag placeholder snaps, and its candidate slots are frozen at drag start

**Decision**: On drag start, compute the list of zero-overlap candidate slots for the dragged panel's size once. On each `onDrag`, find the slot containing the pointer — O(n) over ≤ ~20 slots — and render a ghost outline there, or nothing if the pointer is over no valid slot (FR-005g). On drop with a placeholder visible, the panel **snaps** to that slot; with no placeholder, it stays exactly where dropped.

**Rationale**: FR-005f says the placeholder shows where the panel "would land if released" — that is a promise, so releasing must actually put it there; a purely decorative ghost would be a lie. Freezing the candidate list at drag start is both a performance requirement (`onDrag` fires at pointer rate and already writes to the store on every event) and a correctness one: recomputing mid-drag would make slots appear and vanish as the dragged panel's own moving rect re-entered the obstacle set. Free-form drop outside any slot preserves the existing behavior users already have — this feature adds guidance, it does not take away manual control.

**Alternatives considered**: Advisory-only ghost with free-form drop (breaks the "would land" promise); always snap to nearest slot (removes the ability to park a panel anywhere, a regression against today's behavior); recompute slots per pointer move (wasteful and visually unstable).

---

## D7 — The reopen tray is a left-edge rail that declares itself reserved

**Decision**: A new `PanelDock` component renders on the left edge, vertically centered, holding the "arrange" action and the closed-panel list. It carries `data-panel-reserved` itself. It renders `null` when there are no open panels *and* no closed entries.

**Rationale**: Every other edge is already claimed, which the code confirms: `WorkspaceOverlay` owns top-right (`top-cluster` theme/account) and bottom-right (`bottom-end` chat trigger); `ExtensionToolbar` sits top-right; the weather widget and boundary-confidence badge sit top-left; the panel-hub indicator sits bottom-left. The left edge mid-height is the only uncontested region. Making the dock reserved closes the obvious loop — otherwise the feature that prevents collisions would itself introduce one. The render-nothing-when-empty rule satisfies FR-008 directly and matches the established convention in `ExtensionToolbar` ("renders nothing at all, not an empty frame").

**Alternatives considered**: Bottom-center strip (matches the Windows-taskbar reference image, but a horizontal strip across the bottom steals the widest part of the viewer, directly working against SC-006's "maximize visible viewer area"); folding reopen into `WorkspaceOverlay`'s `right-stack` (couples the panel subsystem to the page shell's control model, and buries a frequently-used affordance one click deeper inside a `CircularAction` disclosure).

---

## D8 — Closed panels are stored as reconstructed `PanelRequest`s, in `floatingPanelStore`

**Decision**: `closePanel` converts the `FloatingPanel` back into the `PanelRequest` that could recreate it (`requestId`/`kind`/`title`/`chrome`/`contextAssociation`, plus `content` or `typeKey`+`data`) and unshifts it onto a `closedPanels` array in the same store, capped at `MAX_CONCURRENT_PANELS` (10). Reopening calls the existing `openPanel(request)`.

**Rationale**: Reopen must produce a panel indistinguishable from a fresh one, and `openPanel` is already the single code path that resolves chrome, runs zod validation, and re-derives context status — routing reopen through it means FR-013 (stale/invalid association on reopen) needs **zero new code**, because `openPanel` already computes `contextStatus` and `markLivePanelKindUnavailable` already handles a withdrawn live-panel kind. Keeping the array in `floatingPanelStore` rather than a sibling store keeps close-and-enqueue a single atomic `set()`; a separate store would need two writes with a window where the panel exists in neither.

The entry deliberately drops `position`, `size`, `zOrder`, and `minimized` — per the spec's Assumptions, a reopened panel is placed fresh because the screen layout has likely changed since it closed.

**Alternatives considered**: Retaining the whole `FloatingPanel` (carries position/z-order/minimize state that must then be deliberately ignored — storing data in order to discard it); a separate `closedPanelStore` (two-store handoff for no benefit); persisting the tray across reloads (contradicts the existing store's explicit session-only convention and the spec's Assumptions).

---

## D9 — Deleting the two hardcoded-offset stopgaps is in scope

**Decision**: Remove the `top: {xs:460, sm:480}` offset from `ExtensionToolbar.tsx` and the `top: {xs:512, sm:532}` offset from `CameraAttitudeWidget.tsx`, restoring both to their natural corner positions, and add `data-panel-reserved` to them.

**Rationale**: Those magic numbers were added earlier in this session as an explicitly-flagged stopgap, with code comments stating the real fix is coordinating through a placement system rather than a second independently-positioned layer. This feature *is* that system. Leaving them would mean the toolbar floats ~460px down the screen for no reason a future reader could discover, and they would drift out of sync the moment `WorkspaceOverlay`'s `right-stack` gains or loses a control — the exact fragility their own comments warn about. Constitution §4 forbids magic numbers with domain meaning surviving as unexplained literals.

**Alternatives considered**: Leaving them as belt-and-braces (two competing positioning systems, with the hardcoded one silently winning); deferring removal to a follow-up (leaves the repo in a state where the stopgap comment points at a system that now exists but is not used — worse than either end state).
