import { Box, Typography } from '@mui/material'
import { useEffect, useRef } from 'react'
import { create } from 'zustand'
import type { CameraState } from '../../../viewer/api/commands'
import type { ExtensionContext } from '../../../viewer/extensions/context'
import { useAvoidReservedCorner } from '../../../viewer/extensions/components/useAvoidReservedCorner'
import { useViewerExtensionStore } from '../../../viewer/extensions/store/viewerExtensionStore'
import { RESERVED_ATTRIBUTE } from '../../../viewer/panels/layout/reservedRegions'
import { copy } from '../copy'
import { EXTENSION_ID } from './SolarAnalysisOverlay'

/** The tilt at which the bubble reaches the edge of its travel — Google Maps' own maximum tilt for
 * vector maps, so the indicator uses the full range rather than an invented one. */
const MAX_TILT_DEGREES = 67.5
const BUBBLE_TRAVEL_PIXELS = 26

interface CameraAttitudeState {
  camera: CameraState | null
  setCamera: (camera: CameraState | null) => void
}

/** The latest camera heading/tilt, fed by the extension-lifetime subscription below. */
export const useCameraAttitudeStore = create<CameraAttitudeState>((set) => ({
  camera: null,
  setCamera: (camera) => set({ camera }),
}))

/**
 * Subscribes to `cameraChanged` for the lifetime of the extension — call once, from `start()`.
 *
 * Found live (2026-09-14): this used to be `context.on(...)` inside the widget's own mount effect.
 * `context.on` records an extension-owned subscription that is only withdrawn when the extension
 * stops, and returns nothing to unsubscribe with — so every remount of the widget (leaving the
 * workspace route and returning, now that extensions keep running across it) added one more
 * listener that was never removed, each calling `setState` on an unmounted component. Owning the
 * subscription at the extension's own lifetime is what `ExtensionContext`'s contract intends.
 *
 * `cameraChanged` fires on heading and tilt changes as they happen (specs/051 FR-026), not only
 * when movement settles, which is what lets the needle track a rotation instead of snapping.
 */
export function subscribeCameraAttitude(context: ExtensionContext): void {
  context.on('cameraChanged', (event) => useCameraAttitudeStore.getState().setCamera(event.camera))
}

/**
 * A compact camera-attitude readout: where true north lies relative to the current view, and how
 * far the camera is tilted from straight down.
 *
 * Deliberately NOT part of the Solar Analysis figures panel. The figures are driven by the time of
 * day — move the time slider and azimuth, altitude and the sun marker all change. These two are
 * driven by the camera instead: they change when the user rotates or tilts the view and are
 * completely unaffected by time. Two different inputs, so two different surfaces.
 */
export function makeCameraAttitudeWidget(context: ExtensionContext) {
  return function CameraAttitudeWidget() {
    const activation = useViewerExtensionStore((s) => s.extensions[EXTENSION_ID]?.activation)
    const camera = useCameraAttitudeStore((s) => s.camera)
    const rootRef = useRef<HTMLDivElement>(null)
    // specs/054 (feedback 2026-09-13): moved to the top-left corner, under the location/weather
    // readout and the site-boundary confidence badge (both `data-panel-reserved`) — the top-right
    // corner is fully claimed by WorkspaceOverlay's page chrome and the extension toolbar.
    const top = useAvoidReservedCorner(rootRef, 'left')

    useEffect(() => {
      // Seed from the engine so the widget is correct the moment it first appears, rather than
      // blank until the user happens to move the camera. A one-off read, not a subscription — the
      // extension-lifetime subscription above keeps it current from then on.
      if (useCameraAttitudeStore.getState().camera) return
      const initial = context.engine.getCameraState()
      if (initial.ok && initial.data) useCameraAttitudeStore.getState().setCamera(initial.data.camera)
    }, [])

    if (activation !== 'active' || !camera) return null

    // The needle points at true north *as seen in the current view*: rotating the camera clockwise
    // by `heading` moves north counter-clockwise on screen by the same amount.
    const northRotationDegrees = -camera.heading
    const bubbleOffsetPixels = Math.max(-1, Math.min(1, camera.tilt / MAX_TILT_DEGREES)) * BUBBLE_TRAVEL_PIXELS

    return (
      // Found live (2026-09-13): the heading/tilt readout used to live INSIDE the circular dial
      // (`overflow: hidden`, 96px wide) — "N 321° · Tilt 0°" at that width routinely got clipped.
      // Restructured into two siblings sharing one absolute position: the dial (unchanged
      // visually) and a small badge below it, outside the dial's clipping region entirely, sized
      // to fit its own text rather than a fixed circle.
      <Box
        ref={rootRef}
        {...{ [RESERVED_ATTRIBUTE]: '' }}
        sx={{ position: 'absolute', top, left: { xs: 16, sm: 24 }, zIndex: 2, width: 96, pointerEvents: 'none' }}
      >
        <Box
          role="group"
          aria-label={copy.cameraAttitudeLabel}
          sx={{
            // Found live (2026-09-13): missing `position: 'relative'` here — every child below
            // uses `position: 'absolute'` expecting THIS 96x96 circle as its containing block, but
            // without it, they positioned against the OUTER wrapper instead, which is TALLER than
            // 96px (it also contains the badge below). That stretched the crosshair/needle/bubble
            // outside the circle's actual bounds — the "distortion" reported live.
            position: 'relative',
            width: 96,
            height: 96,
            borderRadius: '50%',
            border: '1px solid',
            borderColor: 'divider',
            bgcolor: 'background.paper',
            boxShadow: 3,
            overflow: 'hidden',
          }}
        >
          {/* Crosshair — the fixed frame the bubble moves against. */}
          <Box sx={{ position: 'absolute', inset: 8, borderRadius: '50%', border: '1px solid', borderColor: 'divider', opacity: 0.6 }} />
          <Box sx={{ position: 'absolute', left: '50%', top: 10, bottom: 10, width: '1px', bgcolor: 'divider', transform: 'translateX(-50%)' }} />
          <Box sx={{ position: 'absolute', top: '50%', left: 10, right: 10, height: '1px', bgcolor: 'divider', transform: 'translateY(-50%)' }} />

          {/* North needle. `aria-hidden` because the accessible value is published as text in the
              badge below — a rotated triangle conveys nothing to a screen reader. */}
          {/* The rotation goes through `style`, not `sx`: it changes on every camera event, and MUI
              would otherwise generate and inject a fresh CSS class per distinct angle. */}
          <Box
            aria-hidden
            style={{ transform: `rotate(${northRotationDegrees}deg)` }}
            sx={{ position: 'absolute', inset: 0, transition: 'transform 0.12s linear' }}
          >
            <Box
              sx={{
                position: 'absolute',
                left: '50%',
                top: 12,
                width: 0,
                height: 0,
                transform: 'translateX(-50%)',
                borderLeft: '5px solid transparent',
                borderRight: '5px solid transparent',
                borderBottom: '14px solid',
                borderBottomColor: 'error.main',
              }}
            />
            <Typography
              sx={{ position: 'absolute', left: '50%', top: 26, transform: 'translateX(-50%)', fontSize: 9, fontWeight: 700, letterSpacing: '0.5px' }}
            >
              N
            </Typography>
          </Box>

          {/* Tilt bubble — rides toward the bottom of its travel as the camera tilts toward horizon. */}
          <Box
            aria-hidden
            style={{ transform: `translate(-50%, calc(-50% + ${bubbleOffsetPixels}px))` }}
            sx={{
              position: 'absolute',
              left: '50%',
              top: '50%',
              width: 18,
              height: 18,
              borderRadius: '50%',
              bgcolor: 'success.light',
              boxShadow: '0 0 8px rgba(111,207,92,0.65)',
              transition: 'transform 0.25s ease-out',
            }}
          />
        </Box>

        <Typography
          sx={{
            display: 'block',
            mt: 0.5,
            mx: 'auto',
            width: 'fit-content',
            maxWidth: '100%',
            textAlign: 'center',
            fontSize: 9,
            letterSpacing: '0.6px',
            color: 'text.secondary',
            bgcolor: 'background.paper',
            border: '1px solid',
            borderColor: 'divider',
            borderRadius: 1,
            px: 0.75,
            py: 0.25,
            boxShadow: 1,
            whiteSpace: 'nowrap',
          }}
        >
          {copy.cameraAttitudeReadout(camera.heading, camera.tilt)}
        </Typography>
      </Box>
    )
  }
}
