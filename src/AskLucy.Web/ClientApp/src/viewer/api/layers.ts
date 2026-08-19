/** data-model.md "Render Layer" / "GIS/Map Layer" / "Model/Drawing Layer" / "Overlay". */
export type RenderLayerKind = 'gis' | 'model' | 'overlay'

export interface RenderLayer {
  id: string
  kind: RenderLayerKind
  visible: boolean
  zIndex: number
  /** Layer-kind-specific data (e.g. a GIS layer's `center`/`zoom`); opaque to the engine core. */
  metadata: Record<string, unknown>
}

export interface RenderLayerInput {
  /** Caller-supplied id; the engine generates one if omitted. */
  id?: string
  kind: RenderLayerKind
  visible?: boolean
  zIndex?: number
  metadata?: Record<string, unknown>
}

export interface OverlayInput {
  id?: string
  zIndex?: number
  metadata?: Record<string, unknown>
}
