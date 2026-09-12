# Implementation Plan: Viewer Extension Framework

**Branch**: `050-viewer-extension-framework` | **Date**: 2026-09-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/050-viewer-extension-framework/spec.md`

## Summary

Package the viewer's capabilities as independently loadable extensions: a contract (identity, start/stop, optional activate/deactivate), a registry, a loader, and a context that is an extension's only route to the viewer and that tracks every contribution so stopping one withdraws all of it. `ViewerSurface` becomes a host that starts a declared set. The four capabilities it currently mounts move onto the contract, boundary overlay last.

Two findings reshape the approach from what the spec assumed:

1. **Every capability being migrated is a React component**, three of which depend on hooks and store subscriptions. So an extension contributes *component types* declaratively rather than imperatively mounting DOM. This keeps the migrated capabilities essentially unchanged — the single most important property for a feature whose value is structural and whose risk is regression — and makes the spec's readiness-ordering requirement (FR-018/FR-019/FR-020) fall out of React's own reactivity rather than needing a bespoke notification mechanism.

2. **The viewer-embedded toolbar is a genuinely new surface and this feature builds it** (research D6). It is not the workspace overlay: a toolbar embedded in the viewer holds capabilities contributed by extensions and lives and dies with the viewer, while the workspace overlay sits outside it, controls page UI alongside it, and drives it through the published API. `ChatPage` and `WorkspaceOverlay` are untouched here.

This feature is **entirely frontend**. No backend project is touched.

## Technical Context

**Language/Version**: TypeScript ~6.0 (`strict`), React 19.2

**Primary Dependencies**: MUI 9.2, Zustand 5.0 (extension state, matching the existing `viewerEngineStore`/`floatingPanelStore` convention); the existing `IViewerEngine` facade, consumed unchanged

**Storage**: None. Extension lifecycle state is session-scoped in memory, like every other viewer store.

**Testing**: Vitest for unit and a11y; the existing specs/028, specs/038 and specs/042 suites must keep passing as the regression guard

**Target Platform**: Browser — the workspace viewer surface

**Project Type**: Web application, frontend-only for this feature

**Performance Goals**: The viewer must become usable no later than it does today (spec SC-007). Starting extensions must not block first paint.

**Constraints**: No change to the meaning or behaviour of any published viewer command or event (spec FR-012). No new user-facing capability. Behaviour of all four migrated capabilities must be indistinguishable from today (spec FR-034).

**Scale/Scope**: 1 contract, 1 registry, 1 loader, 1 context, 1 store, 2 host components, 4 migrated capabilities, ~14 new modules.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Clean Architecture / Dependency Rule** | PASS, trivially — frontend-only. No Domain, Application or Infrastructure project is touched, so no dependency arrow exists to point the wrong way. |
| **II. SOLID** | PASS, and this feature is largely an OCP exercise: after it, adding a viewer capability means adding an extension module and one declaration line, with no edit to the viewer core, the panel framework, or any other capability. Each extension has one nameable reason to change. |
| **III. Simplicity — DRY, KISS, YAGNI** | PARTIAL — two entries in Complexity Tracking. The activate/deactivate axis has no consumer among the four migrated capabilities, and the framework introduces a second registry alongside specs/049's panel-kind registry. Both are justified there. |
| **IV. Composition over Inheritance** | PASS. An extension is a plain object produced by a factory, not a base class to extend — deliberately unlike the Autodesk model this feature takes its shape from, whose `Autodesk.Viewing.Extension` is a class you inherit. Nothing here needs an is-a relationship. |
| **V. Dependency Inversion & Testability** | PASS. An extension receives its context; it never imports `viewerEngine` directly. That is what makes an extension unit-testable against a fake context with no viewer, no map and no DOM. |
| **VI. Separation of Concerns** | PASS. Registry stores, loader orchestrates lifecycle, context mediates access and records contributions, host renders. No business logic in components. |
| **VII. Convention over Configuration** | PASS. The existing `viewer/` folder layout and Zustand store convention are followed rather than paralleled. The viewer toolbar is a new surface rather than a reuse of `WorkspaceOverlay` — not a departure from this principle but an application of it, since the two serve different owners and lifetimes (research D6); reusing the page-level mechanism for viewer-owned controls would be the convention violation. |
| **VIII. No Silent Failures (NON-NEGOTIABLE)** | PASS — and the main functional risk. A start failure, a stop failure, an unknown declared id and a contribution whose host never appears must each reach the user visibly (spec FR-011, FR-012, FR-013, FR-029, FR-030, FR-031). Research D5 settles how, reusing the indicator pattern already in `ViewerSurface` rather than inventing a notification channel. |
| **§7 UI Principles** | PASS. Contributed controls inherit the accessibility already proven by `CircularAction`/`WorkspaceOverlay` (D6). The failure indicator must meet contrast and be readable in both themes. |
| **§8 Security** | PASS, narrow surface. Extensions are part of the application, not user-installable or remotely loaded (spec Out of Scope), so no untrusted-code boundary is introduced. The extension context is a capability-narrowing boundary, not a security one, and the plan says so rather than overclaiming. |
| **§10 Testing** | PASS. The load-bearing tests are the *existing* ones: specs/028's panel suites, specs/038's POI coverage, specs/042's boundary coverage, and `ViewerSurface.test.tsx` must all keep passing unchanged in substance (spec FR-035). New tests cover the contract, registry, loader, context teardown and failure isolation. |
| **§13 Documentation** | PASS. Contracts under `contracts/`; `viewer/README.md` updated. |

**Gate result: PASS**, with two items tracked in Complexity Tracking.

### Post-Design Re-check

Re-evaluated after Phase 1. The design did not introduce a violation the initial check missed, and it removed two risks:

- **§2.III improved, not worsened, by D1.** Contributing React component types rather than imperative mounts means the four migrated capabilities keep their existing implementations almost verbatim. The simplest migration is also the lowest-risk one, which is rare enough to note.
- **D6 was corrected during review, not after implementation.** An earlier draft proposed routing extension controls into `WorkspaceOverlay` and not building a viewer toolbar at all, on the strength of specs/024's "never a permanent toolbar" comment. That comment governs page-level controls; it does not reach a surface owned by the viewer's own extensions. The two-toolbar distinction is recorded in D6 and in the spec's Clarifications precisely because it is easy to collapse.
- **§2.VIII satisfied without new infrastructure.** D5 reuses the existing `panel-hub-connection-status` Chip pattern already living in `ViewerSurface`, so failure visibility costs no new notification system.
- **§2.I unchanged.** Confirmed frontend-only through design; no backend file appears in the structure below.

No new Complexity Tracking entries.

## Project Structure

### Documentation (this feature)

```text
specs/050-viewer-extension-framework/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── viewer-extension.md
│   └── extension-context.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/viewer/extensions/          # NEW
├── ViewerExtension.ts                # the contract: identity, manifest, start/stop, activate/deactivate
├── registry.ts                       # register once by id; duplicate id is a configuration error
├── loader.ts                         # start/stop by id; idempotent; isolates and surfaces failures
├── context.ts                        # the context handed to start(); records every contribution
├── declared.ts                       # the declared extension set the viewer starts
├── store/
│   └── viewerExtensionStore.ts       # lifecycle state + contributions, read by the hosts
├── components/
│   ├── ExtensionOverlayHost.tsx      # renders contributed overlays
│   ├── ExtensionToolbar.tsx          # the viewer-embedded toolbar (research D6)
│   └── ExtensionFailureNotice.tsx    # the visible failure surface (research D5)
└── builtin/                          # the four migrated capabilities, as extensions
    ├── panelsExtension.tsx
    ├── poiMarkerExtension.tsx
    ├── boundaryConfidenceExtension.tsx
    └── siteBoundaryExtension.tsx     # migrated LAST (spec FR-033)

src/AskLucy.Web/ClientApp/src/features/viewer/components/
├── ViewerSurface.tsx                 # MODIFIED — becomes a host; references no capability
├── POIMarkerOverlay.tsx              # UNCHANGED — contributed by poiMarkerExtension
├── SiteBoundaryOverlay.tsx           # UNCHANGED — contributed by siteBoundaryExtension
└── SiteBoundaryConfidenceBadge.tsx   # UNCHANGED — contributed by boundaryConfidenceExtension

src/AskLucy.Web/ClientApp/src/viewer/extensions/**/*.test.ts(x)   # unit + a11y

# NOT touched by this feature: ChatPage.tsx, WorkspaceOverlay and the workspace-shell
# control primitives. Those are the page-level control surface, which reaches the viewer
# through its published API and is unrelated to extension-contributed controls (research D6).
```

**Structure Decision**: A new `viewer/extensions/` sibling to the existing `viewer/panels/`, `viewer/engine/` and `viewer/store/`, matching the layout convention those established. The four capabilities' component files stay exactly where they are and are *contributed* by thin extension modules rather than relocated into `builtin/` — physically moving them is a pure file-move that adds diff noise to the one feature whose entire risk is regression, and it can happen at any point after the contract is proven. `builtin/` holds only the extension declarations themselves.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| The activate/deactivate state axis has no consumer among the four migrated capabilities (spec US4, FR-002, FR-004) | Adding a second lifecycle axis after extensions exist changes the contract for every extension already written against it — including specs/052's solar analysis, which is explicitly a user-toggleable capability. The cost of defining it now is one optional method pair and one state field; the cost of adding it later is a breaking change to a published contract plus a migration of every implementer. | **Defer it until specs/052 needs it** was rejected on that migration cost alone. This is the one place the feature deliberately builds ahead of demand, and the spec says so in User Story 4's own priority rationale rather than smuggling it in. Worth re-examining at `/speckit-tasks` if the contract turns out to carry more than the optional method pair. |
| A second registry (`viewer/extensions/registry.ts`) alongside specs/049's panel-kind registry (`viewer/panels/registry.ts`) | They key different things for different lifetimes: the panel registry maps a *live panel kind* to a renderer and is populated **by** extensions; the extension registry maps an *extension id* to its factory and is populated at module load. Merging them would make the panel registry's contents depend on its own consumer. | **One generic registry keyed by a namespaced string** was rejected because the two have different value shapes, different validation and different failure modes — an unregistered panel kind degrades to a visible fallback panel, while an unregistered extension id is a configuration error. A shared abstraction would have to special-case both, which is the premature abstraction §2.III warns about. |
