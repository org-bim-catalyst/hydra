import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useViewerEngineStore } from '../../../viewer/store/viewerEngineStore'
import { ViewerSurface } from './ViewerSurface'
import type { GeolocationState } from '../hooks/useGeolocation'

const { useWebGLSupportMock } = vi.hoisted(() => ({ useWebGLSupportMock: vi.fn() }))
vi.mock('../../../hooks/useWebGLSupport', () => ({ useWebGLSupport: useWebGLSupportMock }))

// The real MapRenderTarget dynamically imports the Google Maps JS bootstrap, which has no
// runtime to load against in jsdom (research.md Decision 10) — stubbed here so this test
// covers ViewerSurface's own mode-switching logic, not Google Maps rendering. Invokes the real
// `onError` prop it's given so ViewerSurface's own fallback wiring is exercised too.
vi.mock('../../../viewer/engine/MapRenderTarget', () => ({
  MapRenderTarget: ({ onError }: { onError: () => void }) => (
    <div data-testid="viewer-map-stub">
      <button type="button" onClick={onError}>
        simulate map load failure
      </button>
    </div>
  ),
}))

const initialState = useViewerEngineStore.getState()

function unavailable(): GeolocationState {
  return { status: 'unavailable', latitude: null, longitude: null }
}

function resolving(): GeolocationState {
  return { status: 'resolving', latitude: null, longitude: null }
}

function granted(latitude: number, longitude: number): GeolocationState {
  return { status: 'granted', latitude, longitude }
}

describe('ViewerSurface', () => {
  beforeEach(() => {
    useViewerEngineStore.setState(initialState, true)
    useWebGLSupportMock.mockReset().mockReturnValue(true)
  })

  it('renders the non-interactive fallback when WebGL is unavailable (FR-005)', () => {
    useWebGLSupportMock.mockReturnValue(false)
    render(<ViewerSurface geolocation={resolving()} />)
    expect(screen.getByTestId('viewer-fallback')).toBeInTheDocument()
  })

  it('renders the placeholder while location is still resolving (FR-001/FR-004)', () => {
    render(<ViewerSurface geolocation={resolving()} />)
    expect(screen.getByTestId('viewer-placeholder')).toBeInTheDocument()
  })

  it('transitions to the map content mode once geolocation resolves (FR-007, US2-AC1)', () => {
    render(<ViewerSurface geolocation={granted(51.5074, -0.1278)} />)

    expect(screen.getByTestId('viewer-map-stub')).toBeInTheDocument()
    expect(screen.queryByTestId('viewer-placeholder')).not.toBeInTheDocument()
    expect(useViewerEngineStore.getState().contentMode).toBe('map')
    expect(useViewerEngineStore.getState().layers).toEqual([
      {
        id: 'gis-current-location',
        kind: 'gis',
        visible: true,
        zIndex: 0,
        metadata: { provider: 'google-maps', center: { latitude: 51.5074, longitude: -0.1278 }, zoom: 15 },
      },
    ])
  })

  it('stays on the placeholder and never adds a layer when geolocation is denied/unavailable (FR-008, US2-AC3)', () => {
    render(<ViewerSurface geolocation={unavailable()} />)

    expect(screen.getByTestId('viewer-placeholder')).toBeInTheDocument()
    expect(useViewerEngineStore.getState().contentMode).toBe('placeholder')
    expect(useViewerEngineStore.getState().layers).toEqual([])
  })

  it('reverts to the placeholder if location becomes unavailable after the map was active (FR-012)', () => {
    const { rerender } = render(<ViewerSurface geolocation={granted(51.5074, -0.1278)} />)
    expect(useViewerEngineStore.getState().contentMode).toBe('map')

    rerender(<ViewerSurface geolocation={unavailable()} />)

    expect(screen.getByTestId('viewer-placeholder')).toBeInTheDocument()
    expect(useViewerEngineStore.getState().contentMode).toBe('placeholder')
    expect(useViewerEngineStore.getState().layers).toEqual([])
  })

  it('falls back to the placeholder (never a blank screen) when the map fails to load (spec.md Edge Cases)', () => {
    render(<ViewerSurface geolocation={granted(51.5074, -0.1278)} />)
    expect(useViewerEngineStore.getState().contentMode).toBe('map')

    fireEvent.click(screen.getByRole('button', { name: 'simulate map load failure' }))

    expect(screen.getByTestId('viewer-placeholder')).toBeInTheDocument()
    expect(useViewerEngineStore.getState().contentMode).toBe('placeholder')
    expect(useViewerEngineStore.getState().layers).toEqual([])
  })
})
