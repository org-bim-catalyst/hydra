# Contract: `IViewerEngine` Extensions

**Feature**: `051-viewer-scene-content-api`

Purely additive to specs/027's `contracts/viewer-engine-api.md` — every method below is new;
nothing existing changes signature, return shape, or emitted event (FR-038, FR-039).

## New Commands

| Command | Signature | Notes |
|---|---|---|
| `loadContent` | `(source: ContentSource, placement?: WorldPlacement) => ViewerCommandResult<{ contentId: string }>` | FR-001. Fails gracefully (never throws) for an unsupported format or missing placement — resolves with `ok: false` and the content is left in a `failed` `ViewerContent` record, never left half-loaded (Edge Cases). |
| `replaceContent` | `(contentId: string, source: ContentSource, placement?: WorldPlacement) => ViewerCommandResult` | FR-001, FR-006. The previous content's resources are released before the new content begins loading. Fails if `contentId` does not exist. |
| `unloadContent` | `(contentId: string) => ViewerCommandResult` | FR-001, FR-006. Releases every resource the content held. |
| `listContent` | `() => ViewerCommandResult<{ content: ViewerContent[] }>` | FR-001. Always succeeds (may return an empty list). |
| `getReferencePoint` | `() => ViewerCommandResult<{ referencePoint: ReferencePoint | null }>` | FR-008. `null` before any content has loaded. |
| `getCameraState` | `() => ViewerCommandResult<{ camera: CameraState }>` | FR-025. |
| `getElementInfo` | `(layerId: string, elementId: string) => ViewerCommandResult<{ info: ElementProperties }>` | FR-027, FR-028. Fails if the element is not registered (mirrors `select`'s existing failure shape). |
| `selectAndFrame` | `(layerId: string, elementId: string) => ViewerCommandResult` | FR-029. The one command FR-032 adds to specs/049's action allowlist — see `contracts/action-allowlist-extension.md`. Fails with a stated reason if the element no longer exists (US3 AC4) rather than silently doing nothing. |
| `invalidate` | `() => void` | FR-020. Not a `ViewerCommandResult` command like the others — a fire-and-forget scheduling request, same shape as `on()`'s subscribe/unsubscribe pattern rather than a state-changing command with a success/failure outcome. Safe to call with nothing pending; safe to call after the requesting capability has stopped (FR-024) because a stopped capability holds no live handle to call it from (specs/050's contribution-teardown guarantee). |

## New Events

| Event | Payload | Notes |
|---|---|---|
| `contentLoading` | `{ contentId: string }` | FR-005 — so a UI surface can show a loading indicator immediately, before `contentLoaded`/`contentFailed` resolves. |
| `contentLoaded` | *(reuses the existing `contentLoaded` event, specs/027 — now also fired for `ViewerContent`, keyed by `layerId`, not redefined)* | FR-038 — no new event needed; the existing one already fires when a layer's content is ready. |
| `contentFailed` | `{ contentId: string; reason: ContentFailureReason }` | FR-035. |
| `cameraChanged` | `{ camera: CameraState }` | FR-026, research D6 — fired on the map's `'idle'` event, not per frame. |
| `drawingRequirementConflict` | `{ requirement: DrawingRequirement; requestedBy: string[] }` | FR-017 — fired when two capabilities declare incompatible requirements; the resolution rule (research D3: union-with-report) still applies, this event is the "reported" half. |
| `drawingCallbackFailed` | `{ extensionId: string; message: string }` | FR-019, constitution §2.VIII — fired when a capability's `onFrame` callback (or other drawing-space code the registry invokes on its behalf) throws. Contained by `DrawingSpaceRegistry` (mirroring specs/050's tracked `on()` event-handler containment) so one capability's throwing callback never stops another's or the render loop. |

## Compatibility Statement

Every method and event in specs/027's `contracts/viewer-engine-api.md` keeps its exact current
signature and behavior. `ViewerEngine.contract.test.ts` is the existing, unmodified evidence for
this — this feature adds no parallel test for "nothing existing changed" (research D10).
