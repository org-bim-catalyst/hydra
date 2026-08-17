import type { RenderLayer } from '../api/layers'

/** data-model.md "Overlay" — supplementary visual content (analysis visualizations,
 * AI-generated diagrams) rendered above base map/model layers without modifying them (FR-020).
 * Represented internally as a `RenderLayer` with `kind: 'overlay'` (`viewer/api/layers.ts`,
 * `ViewerEngine.createOverlay`) — this alias exists for call sites that want to talk about
 * overlays specifically rather than layers in general. */
export type Overlay = RenderLayer & { kind: 'overlay' }
