import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/httpClient'
import { useActiveSiteBoundaryStore, type GeoPoint } from '../../store/activeSiteBoundaryStore'
import { viewerSession } from '../session/viewerSession'
import { useGoogleMapsStore } from '../store/googleMapsStore'
import { useViewerEngineStore } from '../store/viewerEngineStore'
import type { EditablePolygonHost } from './editablePolygonController'
import { siteBoundaryEditActions } from './siteBoundaryEditActions'
import { useSiteBoundaryEditStore } from './siteBoundaryEditStore'
import { useSiteBoundaryEditMode } from './useSiteBoundaryEditMode'

const chatsApi = vi.hoisted(() => ({
  getChatById: vi.fn(),
  saveSiteBoundaryEdit: vi.fn(),
  combineSiteBoundaryShape: vi.fn(),
}))
vi.mock('../../features/chat/api/chatsApi', () => chatsApi)

const engine = vi.hoisted(() => ({ setViewMode: vi.fn(), setRotationEnabled: vi.fn(), setFlatMap: vi.fn() }))
vi.mock('../engine/viewerEngineInstance', () => ({ viewerEngine: engine }))

// The real host draws google.maps.Polygon; here every ring is a do-nothing stand-in.
const hostMock = vi.hoisted(() => ({
  createRing: vi.fn(() => ({
    path: {
      getLength: () => 0,
      getAt: () => ({ latitude: 0, longitude: 0 }),
      setAt: vi.fn(),
      insertAt: vi.fn(),
      removeAt: vi.fn(),
      onSetAt: () => () => {},
      onInsertAt: () => () => {},
      onRemoveAt: () => () => {},
    },
    setEditable: vi.fn(),
    setHighlights: vi.fn(),
    onSelect: () => () => {},
    onVertexClick: () => () => {},
    onVertexMenu: () => () => {},
    remove: vi.fn(),
  })),
}))
vi.mock('./googleEditablePolygonHost', () => ({ createGoogleEditablePolygonHost: (): EditablePolygonHost => hostMock }))

const RING: GeoPoint[] = [
  { latitude: 23.586, longitude: 58.392 },
  { latitude: 23.587, longitude: 58.394 },
  { latitude: 23.585, longitude: 58.395 },
  { latitude: 23.586, longitude: 58.392 },
]

const map = {
  getCenter: () => ({ lat: () => 23.5865, lng: () => 58.3935 }),
  getZoom: () => 17.5,
  getHeading: () => 42,
  getTilt: () => 45,
  moveCamera: vi.fn(),
  fitBounds: vi.fn(),
}

const handle = { map, setOutlineVisible: vi.fn() }

const store = () => useSiteBoundaryEditStore.getState()
const session = () => store().session

function showOutline(revision: string | null = 'rev-1') {
  useActiveSiteBoundaryStore.getState().setBoundary({
    siteName: 'Muscat Grand Mall',
    chatId: 'chat-1',
    centroid: { latitude: 23.586, longitude: 58.393 },
    polygon: RING,
    areaSquareMeters: 15_000,
    confidence: 0.7,
    confidenceLevel: 'medium',
    source: 'OsmBoundary',
    sourceDetail: 'x',
    alternativeCandidateNames: [],
    revision,
    isHandEdited: false,
  })
}

const boundaryDto = (overrides: Record<string, unknown> = {}) => ({
  siteName: 'Muscat Grand Mall',
  centroid: { latitude: 23.586, longitude: 58.393 },
  polygon: RING,
  additionalPolygons: [],
  areaSquareMeters: 14_000,
  confidence: 0.7,
  confidenceLevel: 'high',
  source: 'UserCorrected',
  sourceDetail: 'Hand-edited',
  revision: 'rev-2',
  isHandEdited: true,
  ...overrides,
})

function mountHook() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  return renderHook(() => useSiteBoundaryEditMode(), { wrapper })
}

/** A change that leaves the session dirty, as a drag would. */
function makeDirty() {
  act(() => store().applyChange({ op: 'move', ring: 0, index: 1, before: RING[1], after: { latitude: 23.5875, longitude: 58.3945 } }))
}

/**
 * Stands in for MapRenderTarget: switching between the 3D and the flat map rebuilds it, which shows up as a
 * new handle in the store. The new handle shares the spies and the map, so assertions read the same.
 */
let unsubscribeRebuild: (() => void) | null = null

beforeEach(() => {
  vi.clearAllMocks()
  useViewerEngineStore.setState({ flatMap: false, mapTransition: null })
  viewerSession.nextCamera = null
  engine.setFlatMap.mockImplementation((flat: boolean) => useViewerEngineStore.getState().setFlatMap(flat))
  unsubscribeRebuild = useViewerEngineStore.subscribe((state, previous) => {
    if (state.flatMap !== previous.flatMap) useGoogleMapsStore.setState({ handle: { ...handle } as never })
  })
  useGoogleMapsStore.setState({ handle: handle as never, map: map as never })
  showOutline()
})

afterEach(() => {
  unsubscribeRebuild?.()
  cleanup()
  store().end()
  store().consumeRequest()
  store().setNotice(null)
  useActiveSiteBoundaryStore.getState().clearBoundary()
  useGoogleMapsStore.setState({ handle: null, map: null })
})

describe('useSiteBoundaryEditMode', () => {
  describe('entering', () => {
    it('takes the view over, hides the outline and opens a session on the outline in force', async () => {
      mountHook()

      await act(() => siteBoundaryEditActions.start())

      expect(session()).not.toBeNull()
      expect(session()?.baseRevision).toBe('rev-1')
      expect(session()?.rings[0]).toHaveLength(3)
      expect(session()?.viewState).toMatchObject({ zoom: 17.5, heading: 42, tilt: 45 })
      expect(engine.setRotationEnabled).toHaveBeenCalledWith(false)
      expect(engine.setViewMode).toHaveBeenCalledWith('plan')
      expect(handle.setOutlineVisible).toHaveBeenCalledWith(false)
      expect(map.fitBounds).toHaveBeenCalled()
    })

    it('draws the editable rings once the session exists', async () => {
      mountHook()

      await act(() => siteBoundaryEditActions.start())

      expect(hostMock.createRing).toHaveBeenCalledTimes(1)
    })

    it('says so, and opens nothing, when there is no outlined site (never silent)', async () => {
      useActiveSiteBoundaryStore.getState().clearBoundary()
      mountHook()

      await act(() => siteBoundaryEditActions.start())

      expect(session()).toBeNull()
      expect(store().notice).toBe("There's no outlined site to edit.")
      expect(handle.setOutlineVisible).not.toHaveBeenCalled()
    })

    it('refetches the outline when Lucy opened the editor for a different revision', async () => {
      chatsApi.getChatById.mockResolvedValue({ activeBoundary: boundaryDto({ revision: 'rev-5', isHandEdited: false, source: 'OsmBoundary' }) })
      mountHook()

      await act(() => siteBoundaryEditActions.start('rev-5'))

      expect(chatsApi.getChatById).toHaveBeenCalledWith('chat-1')
      expect(session()?.baseRevision).toBe('rev-5')
    })

    it('refetches when the viewer holds no revision, and tells the user if that fails', async () => {
      showOutline(null)
      chatsApi.getChatById.mockRejectedValue(new Error('network down'))
      mountHook()

      await act(() => siteBoundaryEditActions.start())

      expect(session()).toBeNull()
      expect(store().notice).toContain("Couldn't open the outline editor")
      expect(store().notice).toContain('network down')
      expect(handle.setOutlineVisible).toHaveBeenCalledWith(true)
    })

    it("opens on Lucy's request (the siteBoundaryEdit stream event)", async () => {
      mountHook()

      await act(async () => {
        store().requestEdit({ chatId: 'chat-1', revision: 'rev-1' })
      })

      expect(session()).not.toBeNull()
      expect(store().pendingRequest).toBeNull()
    })

    it('is a visible no-op, not a crash, when the map is not ready', async () => {
      useGoogleMapsStore.setState({ handle: null, map: null })
      mountHook()

      await act(async () => {
        siteBoundaryEditActions.start()
      })

      expect(session()).toBeNull()
      expect(store().notice).toContain("isn't ready")
    })
  })

  describe('a different site is shown while editing (FR-030)', () => {
    const otherSite = () =>
      act(() => {
        useActiveSiteBoundaryStore.getState().setBoundary({
          siteName: 'Burjuman mall',
          chatId: 'chat-1',
          centroid: { latitude: 25.25, longitude: 55.3 },
          polygon: RING,
          areaSquareMeters: 40_000,
          confidence: 0.7,
          confidenceLevel: 'medium',
          source: 'OsmBoundary',
          sourceDetail: 'x',
          alternativeCandidateNames: [],
          revision: 'rev-b',
          isHandEdited: false,
        })
      })

    it('ends edit mode, says the unsaved changes were dropped, and does not save', async () => {
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      makeDirty()

      otherSite()

      expect(session()).toBeNull()
      expect(store().notice).toBe('Your unsaved outline changes were dropped because a new site was shown.')
      expect(chatsApi.saveSiteBoundaryEdit).not.toHaveBeenCalled()
      expect(handle.setOutlineVisible).toHaveBeenLastCalledWith(true)
    })

    it('does not pull the camera back to the old site, but restores the mode and rotation', async () => {
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      map.moveCamera.mockClear()
      engine.setViewMode.mockClear()
      engine.setRotationEnabled.mockClear()

      otherSite()

      expect(map.moveCamera).not.toHaveBeenCalled()
      expect(engine.setViewMode).toHaveBeenLastCalledWith('isometric')
      expect(engine.setRotationEnabled).toHaveBeenLastCalledWith(true)
    })

    it('uses a plainer notice when nothing had been changed', async () => {
      mountHook()
      await act(() => siteBoundaryEditActions.start())

      otherSite()

      expect(store().notice).toBe('Outline editing ended because a new site was shown.')
    })

    it('ends edit mode when the outline is cleared altogether', async () => {
      mountHook()
      await act(() => siteBoundaryEditActions.start())

      act(() => useActiveSiteBoundaryStore.getState().clearBoundary())

      expect(session()).toBeNull()
      expect(store().notice).not.toBeNull()
    })

    it('lets the next edit start cleanly on the new site', async () => {
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      otherSite()
      hostMock.createRing.mockClear()

      await act(() => siteBoundaryEditActions.start())

      expect(session()?.siteName).toBe('Burjuman mall')
      expect(session()?.baseRevision).toBe('rev-b')
      expect(hostMock.createRing).toHaveBeenCalledTimes(1)
    })

    it('is not triggered by the save that is redrawing the same site', async () => {
      chatsApi.saveSiteBoundaryEdit.mockResolvedValue({ activeBoundary: boundaryDto(), message: { id: 'm1' } })
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      makeDirty()

      await act(() => siteBoundaryEditActions.done())

      expect(store().notice).toBeNull()
      expect(useActiveSiteBoundaryStore.getState().isHandEdited).toBe(true)
    })
  })

  describe('a dense outline', () => {
    /** A 600-corner circle, like a raster-traced outline. */
    const denseRing = (): GeoPoint[] => {
      const points = Array.from({ length: 600 }, (_, i) => ({
        latitude: 23.586 + (0.0018 * Math.sin((2 * Math.PI * i) / 600)),
        longitude: 58.393 + (0.002 * Math.cos((2 * Math.PI * i) / 600)),
      }))
      return [...points, points[0]]
    }

    const showDense = () =>
      act(() => {
        useActiveSiteBoundaryStore.getState().setBoundary({
          siteName: 'Muscat Grand Mall',
          chatId: 'chat-1',
          centroid: { latitude: 23.586, longitude: 58.393 },
          polygon: denseRing(),
          areaSquareMeters: 40_000,
          confidence: 0.7,
          confidenceLevel: 'medium',
          source: 'OsmBoundary',
          sourceDetail: 'x',
          alternativeCandidateNames: [],
          revision: 'rev-1',
          isHandEdited: false,
        })
      })

    it('is simplified for the session, and the user is told how much', async () => {
      showDense()
      mountHook()

      await act(() => siteBoundaryEditActions.start())

      const corners = session()!.rings[0].length
      expect(corners).toBeLessThan(150)
      expect(corners).toBeGreaterThanOrEqual(3)
      expect(store().notice).toMatch(/^Simplified the outline from 600 to \d+ corners/)
    })

    it('keeps raising the tolerance for a traced outline, and says the real figure', async () => {
      // A long thin strip whose edges zig-zag by a metre: half a metre alone cannot remove that.
      const zigzag: GeoPoint[] = []
      for (let i = 0; i <= 300; i++) zigzag.push({ latitude: 23.586 + (i % 2 === 0 ? 0 : 0.000009), longitude: 58.392 + i * 0.000009 })
      for (let i = 300; i >= 0; i--) zigzag.push({ latitude: 23.5862 + (i % 2 === 0 ? 0 : 0.000009), longitude: 58.392 + i * 0.000009 })
      act(() => {
        useActiveSiteBoundaryStore.getState().setBoundary({
          siteName: 'Muscat Grand Mall',
          chatId: 'chat-1',
          centroid: { latitude: 23.586, longitude: 58.393 },
          polygon: [...zigzag, zigzag[0]],
          areaSquareMeters: 40_000,
          confidence: 0.7,
          confidenceLevel: 'medium',
          source: 'OsmBoundary',
          sourceDetail: 'x',
          alternativeCandidateNames: [],
          revision: 'rev-1',
          isHandEdited: false,
        })
      })
      mountHook()

      await act(() => siteBoundaryEditActions.start())

      expect(session()!.rings[0].length).toBeLessThan(40)
      expect(store().notice).toMatch(/moved by less than 1 m/)
    })

    it('leaves the saved outline untouched when the edit is cancelled', async () => {
      showDense()
      mountHook()
      await act(() => siteBoundaryEditActions.start())

      act(() => siteBoundaryEditActions.cancel())

      expect(useActiveSiteBoundaryStore.getState().polygon).toHaveLength(601)
      expect(chatsApi.saveSiteBoundaryEdit).not.toHaveBeenCalled()
    })

    it('does not touch or announce anything for an ordinary outline', async () => {
      mountHook()

      await act(() => siteBoundaryEditActions.start())

      expect(session()?.rings[0]).toHaveLength(3)
      expect(store().notice).toBeNull()
    })
  })

  describe('a small window', () => {
    const setWidth = (width: number) => Object.defineProperty(window, 'innerWidth', { value: width, configurable: true, writable: true })

    afterEach(() => setWidth(1024))

    it('starts with the floating bar hidden, since the Outline menu has every action', async () => {
      setWidth(500)
      mountHook()

      await act(() => siteBoundaryEditActions.start())

      expect(session()?.toolbarHidden).toBe(true)
    })

    it('starts with the bar shown on a normal window', async () => {
      setWidth(1400)
      mountHook()

      await act(() => siteBoundaryEditActions.start())

      expect(session()?.toolbarHidden).toBe(false)
    })
  })

  describe('Cancel', () => {
    it('drops the changes, shows the outline again and restores the view exactly', async () => {
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      makeDirty()
      map.moveCamera.mockClear()

      act(() => siteBoundaryEditActions.cancel())

      expect(session()).toBeNull()
      expect(handle.setOutlineVisible).toHaveBeenLastCalledWith(true)
      expect(engine.setViewMode).toHaveBeenLastCalledWith('isometric')
      // The 3D map is rebuilt for the way out, and opens at exactly the view the editor found.
      expect(engine.setFlatMap).toHaveBeenLastCalledWith(false, 'Closing the outline editor...')
      expect(viewerSession.nextCamera).toEqual({ latitude: 23.5865, longitude: 58.3935, zoom: 17.5, heading: 42, tilt: 45 })
      expect(engine.setRotationEnabled).toHaveBeenLastCalledWith(true)
      expect(chatsApi.saveSiteBoundaryEdit).not.toHaveBeenCalled()
    })
  })

  describe('the flat map', () => {
    it('switches to the flat map before framing the outline, and edits on the rebuilt map', async () => {
      mountHook()
      const before = useGoogleMapsStore.getState().handle

      await act(() => siteBoundaryEditActions.start())

      expect(engine.setFlatMap).toHaveBeenCalledWith(true, 'Opening the outline editor...')
      expect(useViewerEngineStore.getState().flatMap).toBe(true)
      expect(useGoogleMapsStore.getState().handle).not.toBe(before)
      expect(map.fitBounds).toHaveBeenCalled()
      expect(session()).not.toBeNull()
    })

    it('a site change ends editing on the 3D map without pulling the camera back', async () => {
      mountHook()
      await act(() => siteBoundaryEditActions.start())

      act(() => useActiveSiteBoundaryStore.getState().clearBoundary())

      expect(useViewerEngineStore.getState().flatMap).toBe(false)
      expect(viewerSession.nextCamera).toBeNull()
    })

    it('says so, and goes back to the 3D map, when the flat map never appears', async () => {
      vi.useFakeTimers()
      unsubscribeRebuild?.()
      mountHook()

      const starting = act(() => siteBoundaryEditActions.start())
      await vi.advanceTimersByTimeAsync(21_000)
      await starting
      vi.useRealTimers()

      expect(session()).toBeNull()
      expect(useViewerEngineStore.getState().flatMap).toBe(false)
      expect(store().notice).toMatch(/didn't reload in time/)
    })
  })

  describe('Done', () => {
    it('leaves without a save when nothing changed', async () => {
      mountHook()
      await act(() => siteBoundaryEditActions.start())

      await act(() => siteBoundaryEditActions.done())

      expect(chatsApi.saveSiteBoundaryEdit).not.toHaveBeenCalled()
      expect(session()).toBeNull()
    })

    it('saves the open rings against the revision it started from, then draws the saved outline and restores the view', async () => {
      chatsApi.saveSiteBoundaryEdit.mockResolvedValue({ activeBoundary: boundaryDto(), message: { id: 'm1' } })
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      makeDirty()

      await act(() => siteBoundaryEditActions.done())

      const [chatId, request] = chatsApi.saveSiteBoundaryEdit.mock.calls[0]
      expect(chatId).toBe('chat-1')
      expect(request.expectedRevision).toBe('rev-1')
      expect(request.rings[0]).toHaveLength(3)
      expect(session()).toBeNull()
      expect(useActiveSiteBoundaryStore.getState()).toMatchObject({ isHandEdited: true, revision: 'rev-2', source: 'UserCorrected' })
      expect(handle.setOutlineVisible).toHaveBeenLastCalledWith(true)
      expect(engine.setRotationEnabled).toHaveBeenLastCalledWith(true)
    })

    it('keeps the editor open with the message when the save fails (FR-018)', async () => {
      chatsApi.saveSiteBoundaryEdit.mockRejectedValue(new ApiError(500, 'Request failed', 'The server hiccuped.'))
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      makeDirty()

      await act(() => siteBoundaryEditActions.done())

      expect(session()).not.toBeNull()
      expect(session()?.status).toEqual({ kind: 'error', message: 'The server hiccuped.' })
      expect(session()?.undo).toHaveLength(1)
    })

    it('shows the conflict, with the revision in force, on a 409 (FR-019)', async () => {
      const conflict = new ApiError(409, 'Concurrency conflict', 'changed', undefined, undefined, undefined, { currentRevision: 'rev-9' })
      chatsApi.saveSiteBoundaryEdit.mockRejectedValue(conflict)
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      makeDirty()

      await act(() => siteBoundaryEditActions.done())

      expect(session()?.status).toEqual({ kind: 'conflict', currentRevision: 'rev-9' })
    })

    it('points at the refused ring on a 422 and explains it', async () => {
      const refused = new ApiError(422, 'Site outline rejected', 'Ring 1 crosses itself.', undefined, undefined, undefined, {
        ringIndex: 0,
        reason: 'selfCrossing',
      })
      chatsApi.saveSiteBoundaryEdit.mockRejectedValue(refused)
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      makeDirty()

      await act(() => siteBoundaryEditActions.done())

      expect(session()?.status).toEqual({ kind: 'error', message: 'Ring 1 crosses itself.' })
      expect(session()?.activeRing).toBe(0)
    })

    it('does not start a second save while one is in flight', async () => {
      let finish: (value: unknown) => void = () => {}
      chatsApi.saveSiteBoundaryEdit.mockReturnValue(new Promise((resolve) => (finish = resolve)))
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      makeDirty()

      let first: Promise<void> | undefined
      act(() => {
        first = siteBoundaryEditActions.done()
      })
      await act(() => siteBoundaryEditActions.done())
      finish({ activeBoundary: boundaryDto(), message: { id: 'm1' } })
      await act(async () => {
        await first
      })

      expect(chatsApi.saveSiteBoundaryEdit).toHaveBeenCalledTimes(1)
    })
  })

  describe('Load latest', () => {
    it('rebases the session on the outline now in force and returns to editing', async () => {
      const conflict = new ApiError(409, 'Concurrency conflict', 'changed', undefined, undefined, undefined, { currentRevision: 'rev-9' })
      chatsApi.saveSiteBoundaryEdit.mockRejectedValue(conflict)
      chatsApi.getChatById.mockResolvedValue({ activeBoundary: boundaryDto({ revision: 'rev-9' }) })
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      makeDirty()
      await act(() => siteBoundaryEditActions.done())

      await act(() => siteBoundaryEditActions.loadLatest())

      expect(session()?.baseRevision).toBe('rev-9')
      expect(session()?.status).toEqual({ kind: 'editing' })
      expect(session()?.undo).toHaveLength(0)
      expect(session()?.viewState).toMatchObject({ zoom: 17.5, heading: 42 })
    })

    it('says so when the latest outline cannot be loaded', async () => {
      const conflict = new ApiError(409, 'Concurrency conflict', 'changed', undefined, undefined, undefined, { currentRevision: 'rev-9' })
      chatsApi.saveSiteBoundaryEdit.mockRejectedValue(conflict)
      chatsApi.getChatById.mockRejectedValue(new Error('offline'))
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      makeDirty()
      await act(() => siteBoundaryEditActions.done())

      await act(() => siteBoundaryEditActions.loadLatest())

      expect(session()?.status.kind).toBe('error')
    })
  })

  describe('undo, redo and the Delete key', () => {
    it('undo and redo move through the session', async () => {
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      makeDirty()

      act(() => siteBoundaryEditActions.undo())
      expect(session()?.undo).toHaveLength(0)
      expect(session()?.redo).toHaveLength(1)

      act(() => siteBoundaryEditActions.redo())
      expect(session()?.undo).toHaveLength(1)
    })

    it('says a corner must be selected before deleting or adding one', async () => {
      mountHook()
      await act(() => siteBoundaryEditActions.start())

      act(() => siteBoundaryEditActions.deleteCorner())
      expect(session()?.refusal).toContain('Select a corner first')

      act(() => siteBoundaryEditActions.addCorner())
      expect(session()?.refusal).toContain('Select a corner first')
    })

    it('ignores Backspace typed into a text field, so it never eats the chat box', async () => {
      mountHook()
      await act(() => siteBoundaryEditActions.start())
      act(() => store().selectCorner(1))
      const input = document.createElement('input')
      document.body.appendChild(input)

      act(() => {
        input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Backspace', bubbles: true }))
      })

      expect(session()?.refusal).toBeNull()
      input.remove()
    })
  })

  describe('the Select tool, deleting several corners and the shape tools', () => {
    /** A 200 x 100 m rectangle with a spare corner on the bottom edge. */
    const RECT: GeoPoint[] = [
      { latitude: 23.586, longitude: 58.392 },
      { latitude: 23.586, longitude: 58.3931 },
      { latitude: 23.586, longitude: 58.3942 },
      { latitude: 23.5869, longitude: 58.3942 },
      { latitude: 23.5869, longitude: 58.392 },
    ]

    const showRect = () =>
      act(() => {
        useActiveSiteBoundaryStore.getState().setBoundary({
          siteName: 'Muscat Grand Mall',
          chatId: 'chat-1',
          centroid: { latitude: 23.5865, longitude: 58.393 },
          polygon: [...RECT, RECT[0]],
          areaSquareMeters: 20_000,
          confidence: 0.7,
          confidenceLevel: 'medium',
          source: 'OsmBoundary',
          sourceDetail: 'x',
          alternativeCandidateNames: [],
          revision: 'rev-1',
          isHandEdited: false,
        })
      })

    const begin = async () => {
      showRect()
      mountHook()
      await act(() => siteBoundaryEditActions.start())
    }

    it('toggles the Select tool on and off', async () => {
      await begin()

      act(() => siteBoundaryEditActions.toggleSelectTool())
      expect(session()?.tool).toBe('select')

      act(() => siteBoundaryEditActions.toggleSelectTool())
      expect(session()?.tool).toBe('edit')
    })

    it('Escape hands the map back from the Select tool', async () => {
      await begin()
      act(() => siteBoundaryEditActions.toggleSelectTool())

      act(() => {
        window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
      })

      expect(session()?.tool).toBe('edit')
    })

    it('deletes every selected corner at once, as one undo step', async () => {
      await begin()
      act(() => store().selectCorners([1, 2]))

      act(() => siteBoundaryEditActions.deleteCorner())

      expect(session()?.rings[0]).toHaveLength(3)
      expect(session()?.undo).toHaveLength(1)
      expect(session()?.selectedCorners).toEqual([])
    })

    it('one undo brings all the deleted corners back', async () => {
      await begin()
      act(() => store().selectCorners([1, 2]))
      act(() => siteBoundaryEditActions.deleteCorner())

      act(() => siteBoundaryEditActions.undo())

      expect(session()?.rings[0]).toHaveLength(5)
    })

    it('refuses to delete every corner, leaving the outline as it was', async () => {
      await begin()
      act(() => store().selectCorners([0, 1, 2, 3]))

      act(() => siteBoundaryEditActions.deleteCorner())

      expect(session()?.rings[0]).toHaveLength(5)
      expect(session()?.refusal).toBe('An outline needs at least 3 corners.')
      expect(session()?.selectedCorners).toEqual([0, 1, 2, 3])
    })

    it('deletes the whole selection with the Delete key', async () => {
      await begin()
      act(() => store().selectCorners([1, 2]))

      act(() => {
        window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Delete', bubbles: true }))
      })

      expect(session()?.rings[0]).toHaveLength(3)
    })

    it('asks for a corner first before opening the round or curve dialog', async () => {
      await begin()

      act(() => siteBoundaryEditActions.openShapeDialog('round'))
      expect(store().shapeDialog).toBeNull()
      expect(session()?.refusal).toBe('Select the corner to round first.')

      act(() => siteBoundaryEditActions.openShapeDialog('curve'))
      expect(store().shapeDialog).toBeNull()
      expect(session()?.refusal).toContain('Select the corner at the start of the edge')
    })

    it('opens the circle dialog without any corner selected', async () => {
      await begin()

      act(() => siteBoundaryEditActions.openShapeDialog('circle'))

      expect(store().shapeDialog).toBe('circle')
    })

    it('rounds the selected corner: more corners, one undo step, and the dialog is free to close', async () => {
      await begin()
      act(() => store().selectCorner(3))
      const before = session()!.rings[0].length

      let done = false
      act(() => {
        done = siteBoundaryEditActions.applyShape('round', 15)
      })

      expect(done).toBe(true)
      expect(session()!.rings[0].length).toBeGreaterThan(before + 3)
      expect(session()?.undo).toHaveLength(1)
      expect(session()?.undo[0].op).toBe('replace')
    })

    it('says how big a radius fits, and changes nothing, when it is too big', async () => {
      await begin()
      act(() => store().selectCorner(3))

      let done = true
      act(() => {
        done = siteBoundaryEditActions.applyShape('round', 500)
      })

      expect(done).toBe(false)
      expect(session()?.refusal).toMatch(/too big for this corner/)
      expect(session()?.rings[0]).toHaveLength(5)
      expect(session()?.undo).toHaveLength(0)
    })

    it('curves the edge after the selected corner', async () => {
      await begin()
      act(() => store().selectCorner(0))

      let done = false
      act(() => {
        done = siteBoundaryEditActions.applyShape('curve', 6)
      })

      expect(done).toBe(true)
      expect(session()!.rings[0].length).toBeGreaterThan(5)
    })

    it('replaces the ring with a circle', async () => {
      await begin()

      let done = false
      act(() => {
        done = siteBoundaryEditActions.applyShape('circle', 60)
      })

      expect(done).toBe(true)
      expect(session()?.rings[0]).toHaveLength(72)
    })

    it('will not start an arc without exactly two corners selected', async () => {
      await begin()

      act(() => siteBoundaryEditActions.startArc())
      expect(session()?.tool).toBe('edit')
      expect(session()?.refusal).toContain('Select exactly two corners')

      act(() => store().selectCorners([0, 1, 2]))
      act(() => siteBoundaryEditActions.startArc())
      expect(session()?.tool).toBe('edit')
    })

    it('starts an arc between two selected corners', async () => {
      await begin()
      act(() => store().selectCorners([0, 1]))

      act(() => siteBoundaryEditActions.startArc())

      expect(session()?.tool).toBe('arc')
      expect(session()?.arcAnchors).toEqual([0, 1])
    })

    it('draws the arc through the dropped point, as one undo step, and goes back to editing', async () => {
      await begin()
      act(() => store().selectCorners([0, 1]))
      act(() => siteBoundaryEditActions.startArc())
      const before = session()!.rings[0].length

      let done = false
      act(() => {
        // Below the bottom edge (latitude 23.586), halfway between its ends.
        done = siteBoundaryEditActions.applyArc({ latitude: 23.5858, longitude: 58.39365 })
      })

      expect(done).toBe(true)
      expect(session()!.rings[0].length).toBeGreaterThan(before)
      expect(session()?.undo).toHaveLength(1)
      expect(session()?.undo[0].op).toBe('replace')
      expect(session()?.tool).toBe('edit')
    })

    it('says why, and stays in the arc tool, when the dropped point is in line with the corners', async () => {
      await begin()
      act(() => store().selectCorners([0, 1]))
      act(() => siteBoundaryEditActions.startArc())

      let done = true
      act(() => {
        done = siteBoundaryEditActions.applyArc({ latitude: 23.586, longitude: 58.39365 })
      })

      expect(done).toBe(false)
      expect(session()?.tool).toBe('arc')
      expect(session()?.refusal).toMatch(/straight line/)
    })

    it('Escape cancels the arc tool', async () => {
      await begin()
      act(() => store().selectCorners([0, 1]))
      act(() => siteBoundaryEditActions.startArc())

      act(() => {
        window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
      })

      expect(session()?.tool).toBe('edit')
    })

    it('reports a shape tool asked to act with nothing selected', async () => {
      await begin()

      let done = true
      act(() => {
        done = siteBoundaryEditActions.applyShape('round', 10)
      })

      expect(done).toBe(false)
      expect(session()?.refusal).toBe('Select a corner first.')
    })
  })

  describe('drawing a circle to add or cut', () => {
    const begin = async () => {
      mountHook()
      await act(() => siteBoundaryEditActions.start())
    }

    const CENTRE = { latitude: 23.5862, longitude: 58.3935 }
    /** A small square well away from RING: what the server returns for a circle that touches nothing. */
    const SEPARATE: GeoPoint[] = [
      { latitude: 23.59, longitude: 58.4 },
      { latitude: 23.59, longitude: 58.4004 },
      { latitude: 23.5904, longitude: 58.4004 },
      { latitude: 23.5904, longitude: 58.4 },
    ]

    it('starts the circle tool with the chosen operation', async () => {
      await begin()

      act(() => siteBoundaryEditActions.startCircle('cut'))

      expect(session()?.tool).toBe('circle')
      expect(session()?.circleOperation).toBe('cut')
    })

    it('sends the open rings as they stand and swaps in what the server returns, as one undo step', async () => {
      await begin()
      chatsApi.combineSiteBoundaryShape.mockResolvedValue({ rings: [RING, SEPARATE] })
      act(() => siteBoundaryEditActions.startCircle('add'))

      let done = false
      await act(async () => {
        done = await siteBoundaryEditActions.applyCircle(CENTRE, 40)
      })

      expect(done).toBe(true)
      expect(chatsApi.combineSiteBoundaryShape).toHaveBeenCalledWith('chat-1', expect.objectContaining({ operation: 'Add', centre: CENTRE, radiusMeters: 40 }))
      expect(session()?.rings).toHaveLength(2)
      expect(session()?.undo).toHaveLength(1)
      expect(session()?.undo[0].op).toBe('replaceAll')
      expect(session()?.tool).toBe('edit')
    })

    it('one undo brings back the single ring', async () => {
      await begin()
      chatsApi.combineSiteBoundaryShape.mockResolvedValue({ rings: [RING, SEPARATE] })
      act(() => siteBoundaryEditActions.startCircle('add'))
      await act(async () => {
        await siteBoundaryEditActions.applyCircle(CENTRE, 40)
      })

      act(() => {
        store().undo()
      })

      expect(session()?.rings).toHaveLength(1)
    })

    it("shows the server's reason, and stays in the circle tool, when the cut is refused", async () => {
      await begin()
      chatsApi.combineSiteBoundaryShape.mockRejectedValue(new ApiError(422, 'Site outline rejected', 'That cut would leave a hole in the outline.'))
      act(() => siteBoundaryEditActions.startCircle('cut'))

      let done = true
      await act(async () => {
        done = await siteBoundaryEditActions.applyCircle(CENTRE, 40)
      })

      expect(done).toBe(false)
      expect(session()?.refusal).toContain('hole')
      expect(session()?.tool).toBe('circle')
      expect(session()?.undo).toHaveLength(0)
    })

    it('Escape cancels the circle tool', async () => {
      await begin()
      act(() => siteBoundaryEditActions.startCircle('add'))

      act(() => {
        window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
      })

      expect(session()?.tool).toBe('edit')
    })
  })

  describe('unmounting', () => {
    it('leaves no runtime registered, so the menu explains itself instead of calling a dead map', async () => {
      const { unmount } = mountHook()
      unmount()

      await act(async () => {
        siteBoundaryEditActions.undo()
      })

      expect(store().notice).toContain("isn't ready")
    })
  })
})
