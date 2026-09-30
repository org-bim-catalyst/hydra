import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef } from 'react'
import { ApiError } from '../../api/httpClient'
import { getChatById, saveSiteBoundaryEdit, type ChatActiveBoundary } from '../../features/chat/api/chatsApi'
import { useActiveSiteBoundaryStore, siteRingsOf, type GeoPoint, type SiteBoundarySource } from '../../store/activeSiteBoundaryStore'
import { viewerEngine } from '../engine/viewerEngineInstance'
import { useGoogleMapsStore } from '../store/googleMapsStore'
import { useViewerEngineStore } from '../store/viewerEngineStore'
import {
  createEditablePolygonController,
  type EditablePolygonController,
} from './editablePolygonController'
import { createGoogleEditablePolygonHost } from './googleEditablePolygonHost'
import { registerSiteBoundaryEditRuntime, siteBoundaryEditActions, type SiteBoundaryEditRuntime } from './siteBoundaryEditActions'
import { openRing } from './ringGeometry'
import { useSiteBoundaryEditStore } from './siteBoundaryEditStore'
import { captureViewState, enterPlanForEditing, restoreViewState, type ViewStateDeps } from './viewStateCapture'

const store = () => useSiteBoundaryEditStore.getState()

const messageOf = (error: unknown, fallback: string) =>
  error instanceof ApiError ? (error.detail ?? error.message) : error instanceof Error ? error.message : fallback

/** The chat's outline as the API carries it, put where the viewer draws it from. */
function applyBoundaryToViewer(chatId: string, boundary: ChatActiveBoundary) {
  useActiveSiteBoundaryStore.getState().setBoundary({
    siteName: boundary.siteName,
    chatId,
    centroid: boundary.centroid,
    polygon: boundary.polygon,
    additionalPolygons: boundary.additionalPolygons ?? [],
    areaSquareMeters: boundary.areaSquareMeters,
    confidence: boundary.confidence,
    confidenceLevel: boundary.confidenceLevel,
    source: boundary.source as SiteBoundarySource,
    sourceDetail: boundary.sourceDetail,
    alternativeCandidateNames: [],
    revision: boundary.revision,
    isHandEdited: boundary.isHandEdited,
  })
}

const ringsOf = (boundary: ChatActiveBoundary): GeoPoint[][] => [boundary.polygon, ...(boundary.additionalPolygons ?? [])]

/**
 * specs/079: outline edit mode, end to end. Mounted once, inside the viewer (it needs the live map).
 * Registers the actions the Outline menu and the toolbar call, watches for Lucy's request to open
 * the editor, and keeps the editable polygons in step with the edit session.
 *
 * Every failure here reaches the user: as a notice (entry could not start), or on the session itself
 * (a save that failed or conflicted) - never only the console (constitution section 2 VIII).
 */
export function useSiteBoundaryEditMode() {
  const queryClient = useQueryClient()
  const handle = useGoogleMapsStore((s) => s.handle)
  const inSession = useSiteBoundaryEditStore((s) => s.session !== null)
  const pendingRequest = useSiteBoundaryEditStore((s) => s.pendingRequest)
  const controllerRef = useRef<EditablePolygonController | null>(null)

  useEffect(() => {
    if (!handle) return

    const viewDeps = (): ViewStateDeps => ({
      map: handle.map as unknown as ViewStateDeps['map'],
      engine: viewerEngine,
      camera: () => useViewerEngineStore.getState().camera,
    })

    /** Leaves edit mode: polygons off, outline back, view exactly as it was found. */
    const exit = () => {
      const session = store().session
      controllerRef.current?.unmount()
      controllerRef.current = null
      handle.setOutlineVisible(true)
      if (session) restoreViewState(viewDeps(), session.viewState)
      store().end()
    }

    /** Fetches the chat's outline in force, so an editor never starts from a stale shape. */
    const fetchOutline = async (chatId: string) => {
      const detail = await getChatById(chatId)
      if (!detail.activeBoundary) throw new Error("There's no outlined site to edit.")
      applyBoundaryToViewer(chatId, detail.activeBoundary)
      return detail.activeBoundary
    }

    const start = async (requestedRevision?: string) => {
      if (store().session) return

      try {
        const shown = useActiveSiteBoundaryStore.getState()
        if (!shown.polygon || !shown.chatId || !shown.siteName) {
          store().setNotice("There's no outlined site to edit.")
          return
        }

        // A revision the viewer doesn't hold, or that differs from the one Lucy opened the editor
        // for, means the outline may have changed since: read it again before editing.
        const chatId = shown.chatId
        let revision = shown.revision
        let siteName = shown.siteName
        let rings = siteRingsOf(shown)
        if (!revision || (requestedRevision && requestedRevision !== revision)) {
          const fresh = await fetchOutline(chatId)
          revision = fresh.revision
          siteName = fresh.siteName
          rings = ringsOf(fresh)
        }

        if (store().session) return
        const deps = viewDeps()
        const viewState = captureViewState(deps)
        enterPlanForEditing(deps, rings)
        handle.setOutlineVisible(false)
        store().enter({ chatId, siteName, revision, rings, viewState })
      } catch (error) {
        // Entry failed part-way: whatever was hidden or moved is put back before saying so.
        handle.setOutlineVisible(true)
        store().setNotice(`Couldn't open the outline editor: ${messageOf(error, 'something went wrong.')}`)
      }
    }

    const done = async () => {
      const session = store().session
      if (!session || session.status.kind === 'saving') return

      if (!store().isDirty()) {
        exit()
        return
      }

      store().beginSave()
      try {
        const saved = await saveSiteBoundaryEdit(session.chatId, {
          expectedRevision: session.baseRevision,
          rings: session.rings.map((ring) => openRing(ring)),
        })

        // Polygons off first, so the outline that appears is the saved one, drawn with its animated border.
        controllerRef.current?.unmount()
        controllerRef.current = null
        applyBoundaryToViewer(session.chatId, saved.activeBoundary)
        void queryClient.invalidateQueries({ queryKey: ['chats', session.chatId, 'messages'] })
        void queryClient.invalidateQueries({ queryKey: ['chats', session.chatId, 'detail'] })
        exit()
      } catch (error) {
        if (error instanceof ApiError && error.status === 409) {
          store().conflict(error.currentRevision ?? '')
        } else {
          if (error instanceof ApiError && error.status === 422 && error.ringIndex !== undefined) {
            store().setActiveRing(error.ringIndex)
            controllerRef.current?.setActiveRing(error.ringIndex)
          }
          store().saveFailed(messageOf(error, 'The outline could not be saved.'))
        }
      }
    }

    const loadLatest = async () => {
      const session = store().session
      if (!session) return

      try {
        const fresh = await fetchOutline(session.chatId)
        store().rebase(fresh.revision, ringsOf(fresh))
        const rebased = store().session
        if (rebased) controllerRef.current?.setRings(rebased.rings, rebased.activeRing)
      } catch (error) {
        store().saveFailed(`Couldn't load the latest outline: ${messageOf(error, 'something went wrong.')}`)
      }
    }

    const resync = () => {
      const session = store().session
      if (session) controllerRef.current?.setRings(session.rings, session.activeRing)
    }

    const runtime: SiteBoundaryEditRuntime = {
      start,
      done,
      cancel: exit,
      undo() {
        if (store().undo()) resync()
      },
      redo() {
        if (store().redo()) resync()
      },
      addCorner() {
        const session = store().session
        if (!session) return
        if (session.selectedCorner === null) {
          store().refuse('Select a corner first — the new one is added midway to the next.')
          return
        }
        controllerRef.current?.insertCornerAfter(session.activeRing, session.selectedCorner)
      },
      deleteCorner() {
        const session = store().session
        if (!session) return
        if (session.selectedCorner === null) {
          store().refuse('Select a corner first — right-click it, then choose Delete corner.')
          return
        }
        if (controllerRef.current?.deleteCorner(session.activeRing, session.selectedCorner)) store().selectCorner(null)
      },
      loadLatest,
    }

    const unregister = registerSiteBoundaryEditRuntime(runtime)
    return () => {
      unregister()
    }
  }, [handle, queryClient])

  // Lucy asked for the editor (the `siteBoundaryEdit` stream event): open it.
  useEffect(() => {
    if (!handle || !pendingRequest) return
    const request = store().consumeRequest()
    if (!request) return
    void siteBoundaryEditActions.start(request.revision)
  }, [handle, pendingRequest])

  // The polygons exist exactly while a session does, and are rebuilt when the session comes back
  // after leaving and returning to /studio (FR-029).
  useEffect(() => {
    if (!handle || !inSession) return

    const session = store().session
    if (!session) return

    const controller = createEditablePolygonController(createGoogleEditablePolygonHost(handle.map))
    controller.mount(session.rings, session.activeRing)
    controllerRef.current = controller

    // The active ring changes when the user clicks another one.
    const unsubscribe = useSiteBoundaryEditStore.subscribe((state, previous) => {
      if (state.session && state.session.activeRing !== previous.session?.activeRing) {
        controller.setActiveRing(state.session.activeRing)
      }
    })

    return () => {
      unsubscribe()
      controller.unmount()
      if (controllerRef.current === controller) controllerRef.current = null
    }
  }, [handle, inSession])
}
