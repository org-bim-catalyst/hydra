import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { useAuthStore } from '../../../store/authStore'
import { DECLARED_EXTENSIONS } from '../../../viewer/extensions/declared'
import { useViewerExtensionStore } from '../../../viewer/extensions/store/viewerExtensionStore'
import { resetViewerSession } from '../../../viewer/session/resetViewerSession'
import { viewerSession } from '../../../viewer/session/viewerSession'
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

// The panels extension's overlay only opens a real SignalR connection once a user is signed in
// (useFloatingPanelHub.ts) — every other test here renders signed out, so this never ran until
// the sign-out test below authenticates first. Mirrors useFloatingPanelHub.test.ts's own mock.
vi.mock('@microsoft/signalr', () => {
  class MockHubConnectionBuilder {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return {
        on: () => {},
        onreconnected: () => {},
        onreconnecting: () => {},
        onclose: () => {},
        start: () => Promise.resolve(),
        stop: () => Promise.resolve(),
      }
    }
  }
  return { HubConnectionBuilder: MockHubConnectionBuilder, LogLevel: { Warning: 2 } }
})

const initialViewerState = useViewerEngineStore.getState()
const initialExtensionState = useViewerExtensionStore.getState()

/** Extension stop() calls resolve asynchronously — let them settle before asserting. */
async function flushAsync(): Promise<void> {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0))
  })
}

// specs/057-site-analysis-agent — the panels extension overlay now also mounts
// useSiteAnalysisHub, which calls useQueryClient(); a QueryClientProvider is required here for
// the same reason ChatPage.test.tsx's own render helper provides one.
function renderViewerSurface() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ViewerSurface />
    </QueryClientProvider>,
  )
}

describe('ViewerSurface', () => {
  beforeEach(() => {
    // specs/036-startup-geolocation T009/T016: ViewerSurface now reads from activeLocationStore
    // instead of props. Reset both stores between tests for isolation.
    useViewerEngineStore.setState(initialViewerState, true)
    useActiveLocationStore.getState().clear()
    useWebGLSupportMock.mockReset().mockReturnValue(true)
    // specs/050: each test's render() starts the declared extensions afresh — reset so an
    // earlier test's still-started extensions don't leak into the next one's assertions.
    useViewerExtensionStore.setState(initialExtensionState, true)
    viewerSession.mapContentId = null
    viewerSession.camera = null
  })

  // Extensions now outlive an unmount by design, so each test ends the session explicitly —
  // otherwise one test's running extensions would carry their contributions into the next.
  afterEach(async () => {
    resetViewerSession()
    await flushAsync()
  })

  it('renders the non-interactive fallback when WebGL is unavailable (FR-005)', () => {
    useWebGLSupportMock.mockReturnValue(false)
    renderViewerSurface()
    expect(screen.getByTestId('viewer-fallback')).toBeInTheDocument()
  })

  it('renders the placeholder while location is still resolving (FR-001/FR-004)', () => {
    // Store is empty — no location yet; mirrors the 'resolving' geolocation state.
    renderViewerSurface()
    expect(screen.getByTestId('viewer-placeholder')).toBeInTheDocument()
  })

  it('transitions to the map content mode once geolocation resolves (FR-007, US2-AC1)', () => {
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    renderViewerSurface()

    expect(screen.getByTestId('viewer-map-stub')).toBeInTheDocument()
    expect(screen.queryByTestId('viewer-placeholder')).not.toBeInTheDocument()
    expect(useViewerEngineStore.getState().contentMode).toBe('map')
    // specs/051 T030: the layer id is now generated by `engine.loadContent(...)`, not the fixed
    // constant this test previously hardcoded — asserted structurally instead.
    expect(useViewerEngineStore.getState().layers).toEqual([
      {
        id: expect.any(String),
        kind: 'gis',
        visible: true,
        zIndex: 0,
        metadata: { provider: 'google-maps', center: { latitude: 51.5074, longitude: -0.1278 }, zoom: 15 },
      },
    ])
  })

  it('stays on the placeholder and never adds a layer when geolocation is denied/unavailable (FR-008, US2-AC3)', () => {
    // Store stays empty after clear() — source === null, same as unavailable.
    renderViewerSurface()

    expect(screen.getByTestId('viewer-placeholder')).toBeInTheDocument()
    expect(useViewerEngineStore.getState().contentMode).toBe('placeholder')
    expect(useViewerEngineStore.getState().layers).toEqual([])
  })

  it('reverts to the placeholder if location becomes unavailable after the map was active (FR-012)', () => {
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    renderViewerSurface()
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
    renderViewerSurface()
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
    renderViewerSurface()
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

  // specs/050-viewer-extension-framework T026: ViewerSurface no longer mounts panels/POI/boundary
  // capabilities directly — it starts the declared extension set and renders their contributions
  // through ExtensionOverlayHost. This proves that wiring actually runs end-to-end, not just that
  // the extension modules work in isolation (already covered by their own unit tests).
  it('starts the declared extensions, which render their contributions through the extension host (specs/050)', async () => {
    renderViewerSurface()

    // The panels extension's "Reconnecting…" indicator only mounts once useFloatingPanelHub is
    // rendered — proof the panels extension actually started and contributed its overlay.
    expect(await screen.findByTestId('panel-hub-connection-status')).toBeInTheDocument()
  })

  // Found live (2026-09-14): leaving /studio (e.g. for /admin) used to stop every extension and
  // lose track of the loaded map, so the user came back to a reset workspace stuck on the
  // placeholder. The workspace state now survives the trip.
  it('keeps the extensions running and the map attached when the viewer is remounted after leaving the route', async () => {
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    const first = renderViewerSurface()
    await screen.findByTestId('panel-hub-connection-status')
    const contributionsBefore = useViewerExtensionStore.getState().contributions.length

    first.unmount()
    await flushAsync()

    for (const id of DECLARED_EXTENSIONS) {
      expect(useViewerExtensionStore.getState().extensions[id]?.lifecycle).toBe('started')
    }

    renderViewerSurface()

    expect(screen.getByTestId('viewer-map-stub')).toBeInTheDocument()
    expect(useViewerEngineStore.getState().contentMode).toBe('map')
    // Re-attached to the map already loaded — not a second copy of it.
    expect(useViewerEngineStore.getState().layers).toHaveLength(1)
    expect(useViewerExtensionStore.getState().contributions).toHaveLength(contributionsBefore)
  })

  // specs/050 FR-028, now on sign-out: the next person to sign in on this tab must not inherit the
  // previous user's running extensions, contributions or loaded map.
  it('ends the viewer session when the user signs out', async () => {
    useAuthStore.setState({ accessToken: 'token', userId: 'user-1' })
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    renderViewerSurface()
    await screen.findByTestId('panel-hub-connection-status')

    act(() => {
      useAuthStore.getState().clear()
    })
    await flushAsync()

    for (const id of DECLARED_EXTENSIONS) {
      expect(useViewerExtensionStore.getState().extensions[id]?.lifecycle).toBe('stopped')
    }
    expect(useViewerExtensionStore.getState().contributions).toHaveLength(0)
    expect(useViewerEngineStore.getState().contentMode).toBe('placeholder')
    expect(useViewerEngineStore.getState().layers).toEqual([])
    expect(viewerSession.mapContentId).toBeNull()
  })
})
