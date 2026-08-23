import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { useViewerEngineStore } from '../../../viewer/store/viewerEngineStore'
import { ViewerSurface } from './ViewerSurface'

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

const initialViewerState = useViewerEngineStore.getState()

describe('ViewerSurface', () => {
  beforeEach(() => {
    // specs/036-startup-geolocation T009/T016: ViewerSurface now reads from activeLocationStore
    // instead of props. Reset both stores between tests for isolation.
    useViewerEngineStore.setState(initialViewerState, true)
    useActiveLocationStore.getState().clear()
    useWebGLSupportMock.mockReset().mockReturnValue(true)
  })

  it('renders the non-interactive fallback when WebGL is unavailable (FR-005)', () => {
    useWebGLSupportMock.mockReturnValue(false)
    render(<ViewerSurface />)
    expect(screen.getByTestId('viewer-fallback')).toBeInTheDocument()
  })

  it('renders the placeholder while location is still resolving (FR-001/FR-004)', () => {
    // Store is empty — no location yet; mirrors the 'resolving' geolocation state.
    render(<ViewerSurface />)
    expect(screen.getByTestId('viewer-placeholder')).toBeInTheDocument()
  })

  it('transitions to the map content mode once geolocation resolves (FR-007, US2-AC1)', () => {
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    render(<ViewerSurface />)

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
    // Store stays empty after clear() — source === null, same as unavailable.
    render(<ViewerSurface />)

    expect(screen.getByTestId('viewer-placeholder')).toBeInTheDocument()
    expect(useViewerEngineStore.getState().contentMode).toBe('placeholder')
    expect(useViewerEngineStore.getState().layers).toEqual([])
  })

  it('reverts to the placeholder if location becomes unavailable after the map was active (FR-012)', () => {
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    render(<ViewerSurface />)
    expect(useViewerEngineStore.getState().contentMode).toBe('map')

    // Mirrors ChatPage's useEffect calling clear() when geolocation transitions to 'unavailable'.
    act(() => {
      useActiveLocationStore.getState().clear()
    })

    expect(screen.getByTestId('viewer-placeholder')).toBeInTheDocument()
    expect(useViewerEngineStore.getState().contentMode).toBe('placeholder')
    expect(useViewerEngineStore.getState().layers).toEqual([])
  })

  it('falls back to the placeholder (never a blank screen) when the map fails to load (spec.md Edge Cases)', () => {
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    render(<ViewerSurface />)
    expect(useViewerEngineStore.getState().contentMode).toBe('map')

    fireEvent.click(screen.getByRole('button', { name: 'simulate map load failure' }))

    expect(screen.getByTestId('viewer-placeholder')).toBeInTheDocument()
    expect(useViewerEngineStore.getState().contentMode).toBe('placeholder')
    expect(useViewerEngineStore.getState().layers).toEqual([])
  })

  // T016: agent-confirmation integration — US3 AC1/AC2 (spec 036 §US3)
  it('re-centres the map to an agent-confirmed location, overriding the startup geolocation (US3 AC1/AC2)', () => {
    // Startup geolocation: map already active at device coords.
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    render(<ViewerSurface />)
    expect(useViewerEngineStore.getState().contentMode).toBe('map')

    // Agent confirms a different, user-named location (from __LOCATION__ SSE event via useChatStream).
    act(() => {
      useActiveLocationStore.getState().setFromAgent(25.2048, 55.2708, 'Al Safa 2 Park', 0.97)
    })

    // Viewer must still be in map mode — it doesn't revert to the placeholder.
    expect(screen.getByTestId('viewer-map-stub')).toBeInTheDocument()
    expect(useViewerEngineStore.getState().contentMode).toBe('map')
    // The GIS layer must still have one entry (re-centred, not re-added).
    expect(useViewerEngineStore.getState().layers).toHaveLength(1)
  })
})
