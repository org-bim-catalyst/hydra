# Architecture Notes: Immersive Viewer Platform

Constitution §13 ("design documentation... lives with its spec"). The full reference lives with
the code it documents — `src/AskLucy.Web/ClientApp/src/viewer/README.md` — since that's what a
future contributor editing the engine will actually open; this file is the pointer + the one
design decision worth recording explicitly at the spec level.

## Summary

The viewer is a layered platform (`src/AskLucy.Web/ClientApp/src/viewer/`): typed API contracts
(`api/`) → a facade class implementing them (`engine/ViewerEngine.ts`) → a session-scoped Zustand
store it reads/writes (`store/`) → swappable render targets it delegates camera commands to via
an internal (non-public) `ViewerRenderTargetHandle` interface. See `viewer/README.md` for the full
package layout, the command/event table, and how a future AI-agent integration would use it.

## T102 — ADR decision for the `ViewerRenderTarget` adapter pattern

Constitution §17 requires an ADR when a decision "introduces a new architectural pattern not
already established in the codebase." Evaluated and **declined**: `ViewerRenderTargetHandle` is a
Strategy/Adapter pattern (swap the concrete implementation behind an interface, chosen at
runtime) — the same shape already used extensively in this codebase's backend (`IAIProvider` with
four swappable providers, `IChunkingStrategy`, `IDocumentTextExtractor`,
`IDocumentPreviewGenerator`, and now `IWeatherProvider`). This is that same, already-established
pattern applied on the frontend for the first time, not a new one — and it's a small, internal,
easily-reversible implementation detail (not a datastore, not a public contract, not expensive to
reverse). No ADR filed.

## Known follow-ups (not blocking this feature's completion)

- **Live verification gap**: the Google Maps `WebGLOverlayView`/Three.js bridge
  (`viewer/layers/gis/GoogleMapsGisLayer.ts`) is implemented to match Google's documented sample
  precisely, but has not been runtime-verified against a live, domain-restricted API key in a real
  browser — this environment has neither. Verify via quickstart.md Scenarios 1–2 and 5 once a key
  is configured (`VITE_GOOGLE_MAPS_API_KEY`).
- **60fps profiling (FR-005a/SC-004a)**: the degradation mechanism (T032a — reduced pixel ratio and
  paused auto-rotation on detected low-end/mobile devices) is implemented and unit-tested
  (`GoogleMapsGisLayer.test.ts`), but actual frame-rate measurement requires a real browser/device
  and hasn't been performed here.
