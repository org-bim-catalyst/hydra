import { create } from 'zustand'
import { prefersReducedMotion } from '../../hooks/usePrefersReducedMotion'
import type { RenderLayer } from '../api/layers'
import type { CameraViewMode, MapStyleId } from '../api/commands'

export type ViewerContentMode = 'placeholder' | 'map'

/** data-model.md "Camera/View State". */
export interface CameraViewState {
  mode: CameraViewMode
  rotationEnabled: boolean
}

/** data-model.md "Selection State" — single-selection for this feature (spec.md Assumptions);
 * `null` fields mean nothing is selected (FR-019). */
export interface SelectionState {
  selectedLayerId: string | null
  selectedElementId: string | null
}

interface ViewerEngineState {
  contentMode: ViewerContentMode
  camera: CameraViewState
  selection: SelectionState
  layers: RenderLayer[]
  /** The map/GIS content mode's base rendering style (ROADMAP/SATELLITE/HYBRID). Single source
   * of truth for the map-style control's active-highlight state — read directly rather than
   * mirrored into a second store. */
  mapStyle: MapStyleId
  setContentMode: (mode: ViewerContentMode) => void
  setCamera: (camera: Partial<CameraViewState>) => void
  setSelection: (selection: SelectionState) => void
  setLayers: (layers: RenderLayer[]) => void
  setMapStyle: (mapStyle: MapStyleId) => void
}

/** data-model.md "ViewerSession" — session-scoped only, no `persist` middleware, mirroring
 * `workspaceOverlayStore`'s convention: every visit to the workspace starts on the placeholder
 * (FR-012b, no location/weather/viewer state survives a reload).
 *
 * `camera.rotationEnabled` defaults to `false` when the user has an OS/browser reduced-motion
 * preference at the moment this module first evaluates, and `true` otherwise (FR-016/SC-008) —
 * read imperatively via `prefersReducedMotion()` rather than the `usePrefersReducedMotion` hook,
 * since a Zustand store is created once, outside any React component. */
export const useViewerEngineStore = create<ViewerEngineState>()((set) => ({
  contentMode: 'placeholder',
  camera: {
    mode: 'isometric',
    rotationEnabled: !prefersReducedMotion(),
  },
  selection: { selectedLayerId: null, selectedElementId: null },
  layers: [],
  mapStyle: 'roadmap',
  setContentMode: (contentMode) => set({ contentMode }),
  setCamera: (camera) => set((s) => ({ camera: { ...s.camera, ...camera } })),
  setSelection: (selection) => set({ selection }),
  setLayers: (layers) => set({ layers }),
  setMapStyle: (mapStyle) => set({ mapStyle }),
}))
