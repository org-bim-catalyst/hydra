import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/httpClient'
import { useActiveSiteBoundaryStore, type GeoPoint } from '../../store/activeSiteBoundaryStore'
import { useGoogleMapsStore } from '../store/googleMapsStore'
import type { EditablePolygonHost } from './editablePolygonController'
import { siteBoundaryEditActions } from './siteBoundaryEditActions'
import { useSiteBoundaryEditStore } from './siteBoundaryEditStore'
import { useSiteBoundaryEditMode } from './useSiteBoundaryEditMode'

const chatsApi = vi.hoisted(() => ({
  getChatById: vi.fn(),
  saveSiteBoundaryEdit: vi.fn(),
}))
vi.mock('../../features/chat/api/chatsApi', () => chatsApi)

const engine = vi.hoisted(() => ({ setViewMode: vi.fn(), setRotationEnabled: vi.fn() }))
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
    setHighlight: vi.fn(),
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

beforeEach(() => {
  vi.clearAllMocks()
  useGoogleMapsStore.setState({ handle: handle as never, map: map as never })
  showOutline()
})

afterEach(() => {
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
      expect(map.moveCamera).toHaveBeenCalledWith({ center: { lat: 23.5865, lng: 58.3935 }, zoom: 17.5, heading: 42, tilt: 45 })
      expect(engine.setRotationEnabled).toHaveBeenLastCalledWith(true)
      expect(chatsApi.saveSiteBoundaryEdit).not.toHaveBeenCalled()
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
