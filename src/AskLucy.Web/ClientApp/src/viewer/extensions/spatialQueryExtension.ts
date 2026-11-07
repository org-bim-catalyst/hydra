/**
 * specs/095 (T027) — Spatial Query Extension.
 *
 * Listens for `spatial:featureSelected` events dispatched by `SpatialLayersOverlay` when the
 * user clicks a feature-query marker or polygon. Reads the feature's attributes from the
 * `spatialLayersStore` and surfaces them via a callback so the host component can open
 * `FeatureAttributePanel` without coupling the Google Maps layer to React component state.
 *
 * Usage:
 * ```ts
 * const ext = createSpatialQueryExtension({
 *   onFeatureSelected: (feature, layerId) => setSelectedFeature({ feature, layerId }),
 * })
 * ext.activate()
 * // …later…
 * ext.deactivate()
 * ```
 */

import {
  FEATURE_SELECTED_EVENT,
  type FeatureSelectedDetail,
} from '../../features/viewer/components/SpatialLayersOverlay'
import { type SpatialQueryFeature } from '../../store/spatialLayersStore'

export interface SpatialQueryExtensionOptions {
  /**
   * Called when the user selects a feature. Provides the full feature object (including
   * attributes) and the ID of the layer it belongs to.
   */
  onFeatureSelected: (feature: SpatialQueryFeature, layerId: string) => void
}

export interface SpatialQueryExtension {
  /** Attaches the map-click listener. Idempotent. */
  activate: () => void
  /** Removes the map-click listener. Idempotent. */
  deactivate: () => void
}

/**
 * Factory — creates a spatial-query extension that bridges Google Maps click events on
 * `feature-query` layer entities to the React attribute-panel state.
 */
export function createSpatialQueryExtension(
  options: SpatialQueryExtensionOptions,
): SpatialQueryExtension {
  let active = false

  const handleFeatureSelected = (event: Event) => {
    const customEvent = event as CustomEvent<FeatureSelectedDetail>
    const { feature, layerId } = customEvent.detail
    options.onFeatureSelected(feature, layerId)
  }

  return {
    activate() {
      if (active) return
      document.addEventListener(FEATURE_SELECTED_EVENT, handleFeatureSelected)
      active = true
    },

    deactivate() {
      if (!active) return
      document.removeEventListener(FEATURE_SELECTED_EVENT, handleFeatureSelected)
      active = false
    },
  }
}
