# Phase 1 Data Model: Viewer Extension Framework

**Feature**: `050-viewer-extension-framework` | **Date**: 2026-09-12

No persistence. Everything below is either a build-time declaration or session-scoped in-memory state.

---

## Viewer Extension

A self-contained viewer capability. The viewer starts and stops it; it knows the viewer, the viewer does not know it.

| Field | Type | Rules |
|---|---|---|
| `id` | string | Unique and stable. Namespaced by convention (`viewer.panels`, `viewer.poi-marker`). Appears at most once in the registry. |
| `manifest` | `ExtensionManifest` | What the extension declares about itself. |
| `start` | `(context) => void \| Promise<void>` | Called once when the viewer starts it. Receives the context (its only route to the viewer). May be async; bounded by a timeout (research D4). |
| `stop` | `() => void \| Promise<void>` | Called when the viewer stops it. Contributions are withdrawn by the framework regardless of what this does — it exists for whatever the extension owns that the context never saw. |
| `activate` | `(mode?) => void` | Optional. Only meaningful when `manifest.toggleable`. |
| `deactivate` | `() => void` | Optional. Same. |

An extension is produced by a factory function, not a base class (plan.md, constitution §IV) — there is no is-a relationship to model.

---

## Extension Manifest

What an extension declares about itself, separate from what it does.

| Field | Type | Rules |
|---|---|---|
| `displayName` | string | Human-readable. Used when naming the extension in a failure message (FR-029). |
| `description` | string | One line. |
| `toggleable` | boolean | Default `false`. Activating a non-toggleable extension is rejected visibly (FR-004). |
| `startsWithViewer` | boolean | Default `true`. Whether the viewer starts it as part of the declared set. |

---

## Extension Registry

The catalogue of extensions known to the application, keyed by id.

- Populated at module load, by the extension modules themselves.
- An id appears **at most once** — a second registration under the same id is a configuration error, thrown in development (FR-007, research D9).
- `resolve(id)` returns `undefined` for an unknown id rather than throwing; the *loader* is what surfaces an unknown declared id visibly (FR-013).

Distinct from specs/049's panel-kind registry, which this feature's extensions *populate*. See plan.md Complexity Tracking for why they are not merged.

---

## Declared Extension Set

The ordered list of extension ids the viewer starts when it opens. Changing what the viewer does means changing this list, not the viewer.

- Fixed in the application — not per-user, not per-tenant, no management UI (spec Out of Scope).
- Order matters only for predictable start sequencing and stable control ordering (FR-022); no extension may depend on another having started.

---

## Lifecycle State

Where one extension is, tracked per id in the store.

| State | Meaning |
|---|---|
| `not-started` | Registered, not yet started. |
| `starting` | `start()` called, not yet resolved. A stop arriving here is honoured, and anything the in-flight start contributes is discarded (research D9). |
| `started` | Running. |
| `failed` | `start()` threw or exceeded its timeout. Carries the reason, shown to the user (FR-029). |
| `stopped` | Stopped cleanly; all contributions withdrawn. |

Separately, and only for a `toggleable` extension: `active` / `inactive`. This is a second axis, not a sixth state — a stopped extension is neither.

---

## Contribution

Something an extension added, recorded against its id so it can be withdrawn in full without the author's cooperation (FR-014, FR-015).

| Kind | Carries | Withdrawn by |
|---|---|---|
| `overlay` | A React component type, rendered over the viewer surface | Removing it from the store |
| `toolbarEntry` | An entry rendered in the **viewer-embedded** toolbar this feature builds — not the workspace overlay, which is a separate, page-level surface reaching the viewer through its published API (research D6) | Removing it from the store |
| `livePanelKind` | A panel kind registered into specs/049's panel registry | Calling that registry's `unregister` |
| `eventSubscription` | The unsubscribe function returned by the viewer's `on()` | Calling it |

Every contribution is made through the context, which is what makes the recording automatic. A contribution made before its host exists is held in the store and rendered when the host mounts — it is never discarded (FR-020, research D3).

---

## Extension Context

What a starting extension receives. Its only route to the viewer (FR-011).

| Member | Purpose |
|---|---|
| `engine` | The existing published `IViewerEngine`, passed through unchanged (FR-012) |
| `on(type, handler)` | Subscribe to a viewer event; the unsubscribe is recorded as a contribution |
| `contributeOverlay(component)` | Contribute an overlay |
| `contributeToolbarEntry(entry)` | Contribute an entry to the viewer-embedded toolbar (research D6) |
| `registerLivePanelKind(definition)` | Register a live panel kind into the specs/049 registry |
| `openPanel(request)` | Open a panel |

The context is per-extension: every call knows which extension made it. An extension never imports `viewerEngine` directly — that is what makes it unit-testable against a fake context with no viewer, no map and no DOM (constitution §V).

---

## Validation Summary

| Condition | Where caught | Outcome |
|---|---|---|
| Two extensions registered under one id | Registry, at module load | Configuration error, thrown in development (FR-007) |
| A declared id that was never registered | Loader, at viewer start | Surfaced visibly; remaining extensions still start (FR-013) |
| `start()` throws | Loader | State `failed` with reason; visible, named; others still start (FR-011, FR-029) |
| `start()` exceeds its timeout | Loader | Treated exactly as a throw (research D4) |
| `stop()` throws | Loader | Surfaced and recorded; remaining extensions still stop (FR-012, FR-030) |
| Start when already `started` | Loader | No-op; no duplicate contributions (FR-008) |
| Stop when not `started` | Loader | No-op (FR-008) |
| Stop while `starting` | Loader | Stop honoured; in-flight contributions discarded (FR-010) |
| Activate a non-toggleable extension | Loader | Rejected visibly, not silently ignored (FR-004) |
| An extension throws while handling a viewer event | The tracked `on()` wrapper | Contained and surfaced; other subscribers still receive the event (FR-017) |
