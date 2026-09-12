import { useEffect } from 'react'
import { SiteBoundaryOverlay } from '../../../features/viewer/components/SiteBoundaryOverlay'
import { useGoogleMapsStore } from '../../store/googleMapsStore'
import type { DrawingSpaceHandle } from '../../scene/DrawingSpaceRegistry'
import type { ExtensionContext } from '../context'
import { viewerExtensionRegistry } from '../registry'
import type { ViewerExtension } from '../ViewerExtension'

/** T051 (specs/051 US4) — advances the comet animation's clock via a `context.onFrame()`
 * subscription instead of the map bridge driving it unconditionally every draw, and keeps itself
 * looping by calling `invalidate()` once per frame while active (research D4/FR-022). Renders
 * `SiteBoundaryOverlay` unchanged underneath — this wrapper only adds the animation subscription. */
function makeSiteBoundaryOverlayWithAnimation(drawingSpace: DrawingSpaceHandle) {
  return function SiteBoundaryOverlayWithAnimation() {
    useEffect(() => {
      drawingSpace.onFrame((deltaSeconds) => {
        useGoogleMapsStore.getState().handle?.advanceSiteBoundaryAnimation(deltaSeconds)
        drawingSpace.invalidate()
      })
    }, [])

    return <SiteBoundaryOverlay />
  }
}

/** research D8/FR-033 — migrated last: this capability carries the most post-release history
 * (specs/042's bug-fix rounds, the specs/044 regression). Contributes `SiteBoundaryOverlay`
 * unchanged — its `handle.setSiteBoundary(null)` cleanup path is exactly what specs/044 touched,
 * so it is left untouched here too; `stop()` needs nothing beyond withdrawing the contributions
 * (the drawing space and the overlay), both tracked automatically by the framework. */
export const siteBoundaryExtension: ViewerExtension = {
  id: 'viewer.site-boundary',
  manifest: { displayName: 'Site Boundary', description: 'Highlights the currently resolved site boundary in the map.' },
  start(context: ExtensionContext) {
    const drawingSpace = context.acquireDrawingSpace()
    context.contributeOverlay(makeSiteBoundaryOverlayWithAnimation(drawingSpace))
  },
  stop() {},
}

viewerExtensionRegistry.register(siteBoundaryExtension)
