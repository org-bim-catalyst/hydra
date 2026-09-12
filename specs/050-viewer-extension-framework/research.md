# Phase 0 Research: Viewer Extension Framework

**Feature**: `050-viewer-extension-framework` | **Date**: 2026-09-12

All Technical Context unknowns are resolved below. D3 and D6 were revised after user review on 2026-09-12 — see each for the settled decision.

---

## D1 — How an extension contributes UI, given an imperative lifecycle

**Decision**: An extension contributes **component types**, declaratively, through its context. The host renders whatever is currently contributed. `start()`/`stop()` remain imperative and own non-UI work (event subscriptions, registering a live panel kind).

**Rationale**: All four capabilities being migrated are React components today, and three of them depend on hooks — `POIMarkerOverlay` and `SiteBoundaryOverlay` subscribe to Zustand stores via hook selectors and do their work in `useEffect`; `SiteBoundaryConfidenceBadge` renders themed MUI markup. A contract that required an extension to imperatively mount and unmount DOM in `start()`/`stop()` would force all three to be rewritten as manual `store.subscribe()` plumbing with hand-rolled teardown — a substantial rewrite of working, shipped code, in the one feature whose entire stated risk is regression (spec FR-034, SC-001).

Contributing component types keeps those files **unchanged**. The extension module becomes a declaration: "I am `viewer.poi-marker`, and I contribute this overlay."

**Alternatives rejected**:
- *Imperative DOM mounting, mirroring the Autodesk model this feature takes its shape from* — that model exists because its viewer predates and does not use React. Copying its mechanism into a React application would mean fighting the framework that already solves this, and rewriting three working capabilities to prove the point.
- *Extensions receive a DOM container to render into* — same rewrite cost, plus it strands the contributed UI outside React's tree, losing theme context, store subscriptions and the testing approach every existing test uses.

**Consequence**: this also resolves D2 and largely dissolves FR-018/FR-019/FR-020 — see D3.

---

## D2 — Where extension state lives

**Decision**: A Zustand store, `viewer/extensions/store/viewerExtensionStore.ts`, holding each extension's lifecycle state and the set of live contributions. Hosts subscribe to it.

**Rationale**: Matches the convention already set by `viewerEngineStore`, `floatingPanelStore`, `googleMapsStore` and `activeSiteBoundaryStore` — session-scoped, no persistence, read by components through selector hooks (constitution §VII). It is also what makes D1's declarative contribution work: the host re-renders when contributions change, with no notification mechanism of its own.

**Alternatives rejected**: *A plain module-level object with a subscriber list* — that is a Zustand store, written by hand and worse.

---

## D3 — Readiness ordering (FR-018, FR-020; **FR-019 struck**)

**Decision**: No bespoke readiness-notification mechanism. A contribution made before its host exists is stored, and the host renders it when it mounts — because the host renders *from the store* (D2), not from a one-shot handoff.

**FR-019 is struck from the spec** (user, 2026-09-12). It required the system to *notify* an extension when a contribution host becomes available. FR-018 and FR-020 are satisfied by the design below and are tested; FR-019 is not satisfied by it — nothing notifies anything — so rather than leave a requirement the implementation deliberately ignores, it is removed. It was copied from the reference model, where it exists for a reason that does not apply here (see below), and building it anyway would add a second path to an outcome already guaranteed, exercised by nothing.

**Rationale**: The spec asks for the equivalent of the Autodesk viewer's `onToolbarCreated` hook: a notification fired when a contribution host becomes available, or immediately if it already exists. That hook exists in the reference model because its contributions are imperative — a toolbar button added before the toolbar exists is simply lost, so the framework must tell you when to try again. Under D1 the contribution is data in a store and the host is a subscriber, so "contributed before the host existed" and "contributed after" are the same case, and FR-020's requirement that a contribution is never discarded holds by construction rather than by a callback firing at the right time.

**What still needs testing**, and is easy to mistake for free: that an extension which *starts* before the map exists still works. `POIMarkerOverlay` and `SiteBoundaryOverlay` both read `googleMapsStore` and no-op while `map`/`handle` is null, then act when it populates — they already handle this correctly today, and the migration must not disturb it.

**T002 confirmed (2026-09-12)**: React 19 Strict Mode is active in development (`main.tsx` wraps `<App />` in `<StrictMode>`), so double-invoked effects are the realistic way the loader's idempotency (FR-008) gets exercised — not a hypothetical to design against defensively, but the actual development-mode behaviour. This is also why the "store contribution, subscriber host" shape matters beyond D1's rationale: a double-invoked `start()` under Strict Mode must not double-contribute, which the loader's start-when-started no-op (data-model.md) is what prevents, not this decision on its own.

**Alternatives rejected**: *Implementing a literal `onHostReady` callback* — dead weight under D1, and a second path to the same outcome is exactly the kind of thing that later rots because nothing exercises it.

---

## D4 — Async start, and not blocking the viewer (FR-014, FR-027, SC-007)

**Decision**: `start(context)` may return `void` or `Promise<void>`. The loader starts every declared extension without awaiting them as a group, so a slow one cannot delay the others or first paint. Each start is bounded by a timeout; exceeding it marks that extension failed and surfaces it exactly like a thrown error.

**Rationale**: Spec FR-027 requires the viewer stay usable while extensions start and never wait indefinitely on one. Extensions are part of the application bundle (spec Assumptions), so a slow start is a defect rather than an expected condition — which means the timeout's job is to make that defect *visible and contained*, not to paper over a normal case.

**Open for `/speckit-tasks`**: the timeout value. A few seconds is the right order of magnitude — long enough that no correct extension ever trips it on a slow device, short enough that a hung one is reported while the user is still looking at the screen. None of the four migrated capabilities does async work in start at all, so nothing in this feature exercises the path; it exists for specs/052.

---

## D5 — How a failure reaches the user (FR-011, FR-029, FR-030, FR-031, SC-006)

**Decision**: A small indicator inside the viewer naming the unavailable capabilities, reusing the exact pattern `ViewerSurface` already uses for the panel hub's `panel-hub-connection-status` Chip — unobtrusive, present only when something is wrong.

**Rationale**: specs/049 established there is no global toast or snackbar infrastructure in this application; MUI `Snackbar` is used ad hoc per feature. Inventing a notification system for a failure mode that should never occur in a correct build would be the larger sin. The existing Chip pattern is already the answer to "an ambient viewer-level thing is degraded", it is already accessible, and it is already in the file being modified.

**Alternatives rejected**:
- *A global toast system* — new infrastructure with exactly one consumer.
- *A dialog* — a capability that failed to start does not warrant interrupting the user; the viewer and every other capability still work, which is the whole point of the isolation requirement.
- *Console-only* — forbidden outright by constitution §2.VIII and spec FR-031.

---

## D6 — The viewer toolbar

**Decision**: **Build the viewer-embedded toolbar, as FR-021 specifies.** It is a distinct surface from the workspace overlay, and the two are not interchangeable.

**The distinction that matters** (user, 2026-09-12 — this decision reverses an earlier draft of D6 that conflated them):

| | Viewer-embedded toolbar | External / page toolbar |
|---|---|---|
| Where | Inside the viewer container | Outside the viewer — the workspace overlay cluster |
| Owns | Viewer capabilities, contributed by extensions | Page UI *and* the viewer |
| Populated by | Extensions, at start | `ChatPage`'s hand-composed `workspaceControls` array |
| Reaches the viewer via | Its extension's context | The published viewer API, exactly as `RotationToggleButton` does today |
| Exists today? | **No — this feature builds it** | Yes (`WorkspaceOverlay`, specs/024) |

**Rationale**: An extension is a self-contained viewer capability. Its controls belong to the viewer, appear and disappear with it, and are meaningless when the viewer is not the active surface — which is exactly the property that makes an extension-contributed control different from a page-level one. The workspace overlay is the application's control surface: it coordinates page UI and drives the viewer through the published API from outside. Routing extension controls through it would make a viewer capability's UI outlive and out-scope the viewer, and would mean `ChatPage` rendering controls belonging to capabilities it knows nothing about.

**On specs/024's "never a permanent toolbar"**: that comment governs *workspace* controls — it is the reason page-level controls are reached through the coordinating overlay rather than a fixed application chrome bar. It does not speak to a viewer-embedded surface owned by the viewer's own extensions. An earlier draft of this decision over-applied it and concluded the toolbar should not be built at all; that reading was wrong and is recorded here rather than quietly removed, because the two-surface distinction is the thing a future reader is most likely to collapse again.

**Consequence**: `ViewerSurface` hosts the toolbar; `ChatPage` and `WorkspaceOverlay` are **not touched by this feature**. The toolbar renders from the extension store, so it names no capability (SC-002 holds), and is absent or empty rather than broken when nothing has contributed (FR-023).

**Alternatives rejected**:
- *Contribute into `WorkspaceOverlay` instead* — conflates two surfaces with different owners, scopes and lifetimes, and drags `ChatPage` into every extension's business.
- *Defer control contribution to specs/052* — leaves an extension unable to give the user any way to trigger it, which guts this feature's own "a new capability ships without touching the core" story.

---

## D7 — What the context exposes (FR-011, FR-012)

**Decision**: The context carries the existing `viewerEngine` singleton typed as `IViewerEngine`, plus contribution helpers (`contributeOverlay`, `contributeToolbarEntry`, `registerLivePanelKind`, `openPanel`) and a tracked `on()` for viewer events. Every helper records what it did against the calling extension's id.

**Rationale**: Spec FR-011 makes the context an extension's only route to the viewer, and FR-012 forbids changing command semantics — so the engine is passed through, not wrapped or re-specified. Tracking lives in the helpers rather than in each extension, which is what makes FR-014/FR-015's "withdrawn without relying on the author to remember" true by construction. The reference implementation this feature is modelled on gets this wrong in its own samples — leaking panels and event listeners on unload — which is the concrete reason the tracking is framework-side here.

**Alternatives rejected**: *Hand the extension `viewerEngine` directly and let it subscribe itself* — every `on()` an extension forgot to unsubscribe would leak past `stop()`, and FR-015 would become a documentation request rather than a guarantee.

---

## D8 — Migration order and what physically moves (FR-032, FR-033)

**Decision**: Migrate in the order panels → POI markers → boundary confidence badge → **site boundary overlay last** (FR-033). Each capability's component file **stays where it is**; the extension module contributes it.

**Rationale**: FR-033 mandates boundary-last because that capability carries the most post-release history (specs/042's bug-fix rounds, the specs/044 regression). Keeping the files in place means each migration's diff is the extension declaration plus the removal of one line from `ViewerSurface` — small enough to review honestly, which is what "behaviour indistinguishable" needs. Relocating the files into `builtin/` is a pure rename with no behavioural content and can follow at any time.

**Per-capability notes found in research**:
- **Panels** — `FloatingPanelHost` + `useFloatingPanelHub`. The hook holds the SignalR connection and returns `isLive`; `ViewerSurface` currently renders the reconnecting Chip from it. Both the host and that indicator belong to the panels extension.
- **POI markers** — `POIMarkerOverlay` returns `null` and works purely through `useEffect`; it reads `googleMapsStore.map` and no-ops until it populates.
- **Boundary confidence badge** — the only one of the four that renders visible DOM of its own.
- **Site boundary overlay** — returns `null`, calls `handle.setSiteBoundary(...)`, and cleans up on unmount. Its teardown path is what the specs/044 regression touched; leave it exactly as-is.

---

## D9 — Registry, loader and lifecycle invariants (FR-006 – FR-010)

**Decision**: The registry throws on duplicate id in development, mirroring `panelTypeRegistry`'s existing posture. The loader treats start-when-started and stop-when-not-started as no-ops by checking lifecycle state in the store, and treats stop-while-starting as "honour the stop, then discard whatever the in-flight start contributes."

**Rationale**: FR-007 through FR-010 are all "do the obvious safe thing", and each is a real bug class: a duplicate registration silently replacing an extension, a double start producing two sets of contributions, a stop racing an async start and leaving orphans. The last one is the only subtle case, and D4's async start is what makes it reachable.

**Alternatives rejected**: *Let the loader assume it is never called twice* — that assumption breaks the moment React 19 Strict Mode double-invokes an effect in development, which is exactly where a framework like this gets exercised first.

---

## D10 — What `ViewerSurface` keeps

**Decision**: The location-to-map wiring in `ViewerSurface`'s `useEffect` — adding the GIS layer, the `fitBounds`/`zoomToAltitude`/`zoomToLocation` priority, reverting to the placeholder — **stays in the host**, unchanged.

**Rationale**: Spec Assumptions already say so, and specs/051 is what makes content loading driveable. Moving it now would mean inventing the content-command design a spec ahead of schedule. `ViewerSurface` keeping this is not a violation of SC-002: it references the viewer *engine*, not any capability.

**Consequence**: after this feature `ViewerSurface` still contains real logic. "Thin host" means it references no individual capability — not that it is empty.
