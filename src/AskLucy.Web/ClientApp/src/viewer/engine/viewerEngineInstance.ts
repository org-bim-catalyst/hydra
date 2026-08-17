import { ViewerEngine } from './ViewerEngine'

/** The single `ViewerEngine` instance for this browser tab (data-model.md "Viewer Session") — a
 * module-level singleton, mirroring `viewerEngineStore`'s own singleton pattern, so
 * `ViewerSurface` (which mounts the render targets) and the workspace toolbar controls
 * (`workspaceControls.tsx`, `RotationToggleButton.tsx`, which issue camera commands) share the
 * exact same instance without prop drilling through `ChatPage`. */
export const viewerEngine = new ViewerEngine()

declare global {
  interface Window {
    __askLucyViewerEngine?: ViewerEngine
  }
}

// US6 (FR-024, SC-006, contracts/viewer-engine-api.md "Verification"): lets a developer invoke
// every documented command directly from the browser devtools console, proving the contract
// works end-to-end with zero AI-agent code involved. Development builds only — never shipped to
// production (constitution §8, no internal API surface exposed to end users).
if (import.meta.env.DEV && typeof window !== 'undefined') {
  window.__askLucyViewerEngine = viewerEngine
}
