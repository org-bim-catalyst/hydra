/** data-model.md "Content Source" — where content comes from. Never an arbitrary external
 * address (spec Assumptions); `fileId` resolves through the platform's existing signed-URL file
 * access mechanism. */
export type ContentSource =
  | { kind: 'gis'; provider: 'google-maps'; center: { latitude: number; longitude: number }; zoom?: number }
  | { kind: 'model'; format: 'gltf'; fileId: string }

/** data-model.md "World Placement" — how content sits in the world, relative to the one
 * `ReferencePoint` (scene/SceneAnchor.ts). `heightMetres` is above ground (0 = ground level);
 * `orientationDegrees` is rotation about the Up axis (0 = north-facing); `scale` is a multiplier
 * on authored scale (1 = unscaled). FR-011 requires all four — position, height, orientation and
 * scale — to actually be applied to loaded content, not just carried as data. */
export interface WorldPlacement {
  latitude: number
  longitude: number
  heightMetres: number
  orientationDegrees: number
  scale: number
}

/** data-model.md "Viewer Content" — the lifecycle `RenderLayer` alone cannot express (research
 * D1). Wraps a `RenderLayer` (specs/027) with load state, format validation and placement. */
export type ContentLoadState = 'loading' | 'loaded' | 'failed'

/** A closed set (FR-035) so each case can be worded distinctly, rather than a raw thrown-error
 * string reaching the user. */
export type ContentFailureReason = 'unsupported-format' | 'unreachable-or-corrupt' | 'unplaceable'

export interface ViewerContent {
  id: string
  layerId: string
  source: ContentSource
  placement: WorldPlacement | null
  loadState: ContentLoadState
  /** Present only when loadState === 'failed' (FR-035). */
  failureReason: ContentFailureReason | null
}
