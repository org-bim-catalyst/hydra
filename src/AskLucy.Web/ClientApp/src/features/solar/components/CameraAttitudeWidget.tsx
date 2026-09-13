import { Box, Typography } from '@mui/material'
import { useEffect, useRef, useState } from 'react'
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

/**
 * A compact camera-attitude readout: where true north lies relative to the current view, and how
 * far the camera is tilted from straight down.
 *
 * Deliberately NOT part of the Solar Analysis figures panel. The figures are driven by the time of
 * day — move the time slider and azimuth, altitude and the sun marker all change. These two are
 * driven by the camera instead: they change when the user rotates or tilts the view and are
 * completely unaffected by time. Two different inputs, so two different surfaces.
 *
 * Reads `cameraChanged` (specs/051 FR-026) rather than polling. That event now fires on heading
 * and tilt changes as they happen, not only when movement settles, which is what lets the needle
 * track a rotation instead of snapping to its end state.
 */
export function makeCameraAttitudeWidget(context: ExtensionContext) {
  return function CameraAttitudeWidget() {
    const activation = useViewerExtensionStore((s) => s.extensions[EXTENSION_ID]?.activation)
    const [camera, setCamera] = useState<CameraState | null>(null)
    const rootRef = useRef<HTMLDivElement>(null)
    // specs/054 (feedback 2026-09-13): moved to the top-left corner, under the location/weather
    // readout and the site-boundary confidence badge (both `data-panel-reserved`) — the top-right
    // corner is fully claimed by WorkspaceOverlay's page chrome and the extension toolbar.
    const top = useAvoidReservedCorner(rootRef, 'left')

    useEffect(() => {
      // Seed from the engine so the widget is correct the moment it appears, rather than blank
      // until the user happens to move the camera.
      const initial = context.engine.getCameraState()
      if (initial.ok && initial.data) setCamera(initial.data.camera)

      context.on('cameraChanged', (event) => setCamera(event.camera))
    }, [])

    if (activation !== 'active' || !camera) return null

    // The needle points at true north *as seen in the current view*: rotating the camera clockwise
    // by `heading` moves north counter-clockwise on screen by the same amount.
    const northRotationDegrees = -camera.heading
    const bubbleOffsetPixels = Math.max(-1, Math.min(1, camera.tilt / MAX_TILT_DEGREES)) * BUBBLE_TRAVEL_PIXELS

    return (
      <Box
        ref={rootRef}
        role="group"
        aria-label={copy.cameraAttitudeLabel}
        {...{ [RESERVED_ATTRIBUTE]: '' }}
        sx={{
          position: 'absolute',
          top,
          left: { xs: 16, sm: 24 },
          zIndex: 2,
          width: 96,
          height: 96,
          borderRadius: '50%',
          border: '1px solid',
          borderColor: 'divider',
          bgcolor: 'background.paper',
          boxShadow: 3,
          pointerEvents: 'none',
          overflow: 'hidden',
        }}
      >
        {/* Crosshair — the fixed frame the bubble moves against. */}
        <Box sx={{ position: 'absolute', inset: 8, borderRadius: '50%', border: '1px solid', borderColor: 'divider', opacity: 0.6 }} />
        <Box sx={{ position: 'absolute', left: '50%', top: 10, bottom: 10, width: '1px', bgcolor: 'divider', transform: 'translateX(-50%)' }} />
        <Box sx={{ position: 'absolute', top: '50%', left: 10, right: 10, height: '1px', bgcolor: 'divider', transform: 'translateY(-50%)' }} />

        {/* North needle. `aria-hidden` because the accessible value is published as text below —
            a rotated triangle conveys nothing to a screen reader. */}
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

        <Typography
          sx={{ position: 'absolute', bottom: 4, left: 0, right: 0, textAlign: 'center', fontSize: 9, letterSpacing: '0.6px', color: 'text.secondary' }}
        >
          {copy.cameraAttitudeReadout(camera.heading, camera.tilt)}
        </Typography>
      </Box>
    )
  }
}
