import { act, cleanup, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ExtensionContext } from '../../../viewer/extensions/context'
import { useViewerExtensionStore } from '../../../viewer/extensions/store/viewerExtensionStore'
import { makeCameraAttitudeWidget } from './CameraAttitudeWidget'
import { EXTENSION_ID } from './SolarAnalysisOverlay'

function makeContext(camera: { heading: number; tilt: number } | null) {
  const handlers: Record<string, (event: unknown) => void> = {}
  const context = {
    engine: {
      getCameraState: () =>
        camera
          ? { ok: true as const, data: { camera: { latitude: 25.2, longitude: 55.27, zoom: 18, ...camera } } }
          : { ok: false as const, error: 'no target' },
    },
    on: vi.fn((type: string, handler: (event: unknown) => void) => {
      handlers[type] = handler
    }),
    contributeOverlay: vi.fn(),
    contributeToolbarEntry: vi.fn(),
    registerLivePanelKind: vi.fn(),
    openPanel: vi.fn(),
    acquireDrawingSpace: vi.fn(),
  } as unknown as ExtensionContext
  return { context, handlers }
}

function setActivation(activation: 'active' | 'inactive') {
  useViewerExtensionStore.setState({
    extensions: {
      [EXTENSION_ID]: { lifecycle: 'started', failureReason: null, activation, lastEventError: null },
    },
  })
}

afterEach(cleanup)

describe('CameraAttitudeWidget', () => {
  beforeEach(() => setActivation('active'))

  it('renders the camera heading and tilt as readable text, not only as a rotated needle', () => {
    const { context } = makeContext({ heading: 90, tilt: 55 })
    const Widget = makeCameraAttitudeWidget(context)
    render(<Widget />)

    // The needle and bubble are aria-hidden decoration; the values must still be readable
    // (constitution §7 — a rotated triangle conveys nothing to assistive technology).
    expect(screen.getByText('N 90° · Tilt 55°')).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'View orientation' })).toBeInTheDocument()
  })

  it('points the needle opposite the camera heading, so it indicates true north on screen', () => {
    const { context } = makeContext({ heading: 90, tilt: 0 })
    const Widget = makeCameraAttitudeWidget(context)
    const { container } = render(<Widget />)

    // Rotating the camera clockwise moves north counter-clockwise in the view.
    expect(container.innerHTML).toContain('rotate(-90deg)')
  })

  it('tracks a camera change as it happens rather than waiting for the next read', () => {
    const { context, handlers } = makeContext({ heading: 0, tilt: 0 })
    const Widget = makeCameraAttitudeWidget(context)
    render(<Widget />)
    expect(screen.getByText('N 0° · Tilt 0°')).toBeInTheDocument()

    act(() => {
      handlers.cameraChanged?.({ camera: { latitude: 25.2, longitude: 55.27, zoom: 18, heading: 180, tilt: 40 } })
    })

    expect(screen.getByText('N 180° · Tilt 40°')).toBeInTheDocument()
  })

  it('renders nothing while the analysis is not active', () => {
    setActivation('inactive')
    const { context } = makeContext({ heading: 0, tilt: 0 })
    const Widget = makeCameraAttitudeWidget(context)
    const { container } = render(<Widget />)

    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing when no camera state is available yet, rather than a misleading zero reading', () => {
    const { context } = makeContext(null)
    const Widget = makeCameraAttitudeWidget(context)
    const { container } = render(<Widget />)

    expect(container).toBeEmptyDOMElement()
  })
})
