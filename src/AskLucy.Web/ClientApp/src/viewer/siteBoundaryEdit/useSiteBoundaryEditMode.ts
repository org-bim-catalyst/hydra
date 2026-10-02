import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef } from 'react'
import { ApiError } from '../../api/httpClient'
import { combineSiteBoundaryShape, getChatById, saveSiteBoundaryEdit, type ChatActiveBoundary } from '../../features/chat/api/chatsApi'
import { useActiveSiteBoundaryStore, siteRingsOf, type GeoPoint, type SiteBoundarySource } from '../../store/activeSiteBoundaryStore'
import { viewerEngine } from '../engine/viewerEngineInstance'
import { useGoogleMapsStore } from '../store/googleMapsStore'
import { useViewerEngineStore } from '../store/viewerEngineStore'
import {
  createEditablePolygonController,
  type EditablePolygonController,
} from './editablePolygonController'
import { useCornerMenuStore } from './cornerMenuStore'
import { createGoogleEditablePolygonHost } from './googleEditablePolygonHost'
import { registerSiteBoundaryEditRuntime, siteBoundaryEditActions, type SiteBoundaryEditRuntime } from './siteBoundaryEditActions'
import { DENSE_RING_CORNERS, openRing, simplifyDenseRing } from './ringGeometry'
import { arcThroughPoint, circleRing, curveEdge, ringCentre, roundCorner } from './ringShapes'
import { useSiteBoundaryEditStore } from './siteBoundaryEditStore'
import { captureViewState, enterPlanForEditing, restoreViewState, type ViewStateDeps } from './viewStateCapture'

const store = () => useSiteBoundaryEditStore.getState()

/** Below this window width the edit bar starts hidden (the Outline menu has every action). */
const SMALL_SCREEN_PX = 720

const messageOf = (error: unknown, fallback: string) =>
  error instanceof ApiError ? (error.detail ?? error.message) : error instanceof Error ? error.message : fallback

/** The chat's outline as the API carries it, put where the viewer draws it from. */
export function applyBoundaryToViewer(chatId: string, boundary: ChatActiveBoundary) {
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
  // Identifies WHICH session is open, so a new one (another site, or Load latest) rebuilds the
  // editable polygons instead of leaving the previous session's rings on the map.
  const sessionKey = useSiteBoundaryEditStore((s) => (s.session ? `${s.session.chatId}|${s.session.siteName}|${s.session.baseRevision}` : ''))
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
    const exit = (options: { keepCamera?: boolean } = {}) => {
      const session = store().session
      controllerRef.current?.unmount()
      controllerRef.current = null
      handle.setOutlineVisible(true)
      if (session) restoreViewState(viewDeps(), session.viewState, options)
      store().end()
    }

    // FR-030: a different site replacing the one being edited (Lucy moved to another place, or the
    // outline was cleared) ends edit mode without saving. The old site's corners must never stay
    // editable over the new site, and the user is told why their edit is gone.
    const unsubscribeBoundary = useActiveSiteBoundaryStore.subscribe((state) => {
      const session = store().session
      if (!session || session.status.kind === 'saving') return

      const sameSite = state.polygon !== null && state.siteName === session.siteName && state.chatId === session.chatId
      if (sameSite) return

      const hadChanges = store().isDirty()
      exit({ keepCamera: true })
      store().setNotice(
        hadChanges
          ? 'Your unsaved outline changes were dropped because a new site was shown.'
          : 'Outline editing ended because a new site was shown.',
      )
    })

    /** Fetches the chat's outline in force, so an editor never starts from a stale shape. */
    const fetchOutline = async (chatId: string) => {
      const detail = await getChatById(chatId)
      if (!detail.activeBoundary) throw new Error("There's no outlined site to edit.")
      applyBoundaryToViewer(chatId, detail.activeBoundary)
      return detail.activeBoundary
    }

    /**
     * When this tab comes back into view, shows the outline as it now stands. Another tab may have reset or
     * edited it meanwhile, and this one would otherwise keep drawing the old shape. Never while editing: an
     * open session has its own conflict handling when it is saved.
     */
    const refreshShownOutline = async () => {
      const shown = useActiveSiteBoundaryStore.getState()
      if (store().session || !shown.chatId || !shown.polygon) return

      try {
        const latest = (await getChatById(shown.chatId)).activeBoundary
        const now = useActiveSiteBoundaryStore.getState()
        if (store().session || !latest || latest.siteName !== now.siteName || latest.revision === now.revision) return
        applyBoundaryToViewer(shown.chatId, latest)
      } catch (error) {
        // A background check: the outline on screen is still a valid one, and the next time the tab is focused retries.
        console.warn('Could not check for a newer site outline', error)
      }
    }
    const onTabVisible = () => {
      if (document.visibilityState === 'visible') void refreshShownOutline()
    }
    document.addEventListener('visibilitychange', onTabVisible)
    window.addEventListener('focus', onTabVisible)

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

        // A traced outline can carry hundreds of tiny corners, too many handles to work with. Corners
        // that do not change the shape are dropped for the editing session only: Cancel leaves the
        // saved outline exactly as it was, and the user is told what was done.
        const before = rings.reduce((sum, ring) => sum + openRing(ring).length, 0)
        const simplified = rings.map((ring) => simplifyDenseRing(ring))
        rings = simplified.map((result) => result.ring)
        const after = rings.reduce((sum, ring) => sum + ring.length, 0)
        const tolerance = Math.max(0, ...simplified.map((result) => result.toleranceMeters))
        const stillDense = rings.some((ring) => ring.length > DENSE_RING_CORNERS)

        const deps = viewDeps()
        const viewState = captureViewState(deps)
        enterPlanForEditing(deps, rings)
        handle.setOutlineVisible(false)
        store().enter({ chatId, siteName, revision, rings, viewState })
        if (after < before) {
          store().setNotice(
            `Simplified the outline from ${before} to ${after} corners so it's easier to edit (the shape moved by less than ${tolerance} m).` +
              (stillDense ? ' It still has many corners - use the Select tool to delete several at once.' : ''),
          )
        } else if (stillDense) {
          // Said aloud rather than left to look like nothing happened: the user will otherwise face hundreds of handles with no explanation.
          store().setNotice("This outline has a very large number of corners and couldn't be simplified without changing its shape. Use the Select tool to delete several at once.")
        }
        // A small window has no room for the floating bar over the top cards; every action is in the Outline menu.
        if (window.innerWidth < SMALL_SCREEN_PX) store().setToolbarHidden(true)
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
      cancel: () => exit(),
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
      nudgeCorner(eastMeters, northMeters) {
        const session = store().session
        if (!session) return
        if (session.selectedCorner === null) {
          store().refuse('Select a corner first, then move it with the arrow keys.')
          return
        }
        controllerRef.current?.moveCorner(session.activeRing, session.selectedCorner, eastMeters, northMeters)
      },
      deleteCorner() {
        const session = store().session
        if (!session) return
        if (session.selectedCorners.length === 0) {
          store().refuse('Select a corner first \u2014 click it, or use the Select tool to draw a box around several.')
          return
        }
        const controller = controllerRef.current
        const deleted = session.selectedCorners.length > 1
          ? controller?.deleteCorners(session.activeRing, session.selectedCorners)
          : controller?.deleteCorner(session.activeRing, session.selectedCorners[0])
        if (deleted) store().selectCorner(null)
      },
      toggleSelectTool() {
        const session = store().session
        if (session) store().setTool(session.tool === 'select' ? 'edit' : 'select')
      },
      startCircle(operation) {
        if (store().session) store().beginCircle(operation)
      },
      async applyCircle(centre, radiusMeters) {
        const session = store().session
        const controller = controllerRef.current
        if (!session || !controller || !session.circleOperation) return false

        try {
          const result = await combineSiteBoundaryShape(session.chatId, {
            rings: session.rings.map((ring) => openRing(ring)),
            operation: session.circleOperation === 'add' ? 'Add' : 'Cut',
            centre,
            radiusMeters,
          })

          // The session may have ended (or moved to another site) while the server was working.
          if (store().session?.chatId !== session.chatId) return false
          if (!controller.replaceAllRings(result.rings)) return false

          store().setTool('edit')
          return true
        } catch (error) {
          // A refusal the user can act on (a hole, nothing left) arrives as the server's own message.
          store().refuse(messageOf(error, 'The circle could not be applied.'))
          return false
        }
      },
      startArc() {
        const session = store().session
        if (!session) return
        if (session.selectedCorners.length !== 2) {
          store().refuse('Select exactly two corners first — Ctrl-click each one, or drag a box around them with the Select tool.')
          return
        }
        store().beginArc([session.selectedCorners[0], session.selectedCorners[1]])
      },
      applyArc(through) {
        const session = store().session
        const controller = controllerRef.current
        if (!session || !controller || !session.arcAnchors) return false

        const result = arcThroughPoint(session.rings[session.activeRing] ?? [], session.arcAnchors[0], session.arcAnchors[1], through)
        if ('refusal' in result) {
          store().refuse(result.refusal)
          return false
        }
        if (!controller.replaceRing(session.activeRing, result.ring)) return false

        store().setTool('edit')
        return true
      },
      openShapeDialog(tool) {
        const session = store().session
        if (!session) return
        if (tool !== 'circle' && session.selectedCorner === null) {
          store().refuse(tool === 'round' ? 'Select the corner to round first.' : 'Select the corner at the start of the edge to curve first.')
          return
        }
        store().setShapeDialog(tool)
      },
      applyShape(tool, value) {
        const session = store().session
        const controller = controllerRef.current
        if (!session || !controller) return false

        const ring = session.rings[session.activeRing] ?? []
        const corner = session.selectedCorner
        const result =
          tool === 'circle'
            ? circleRing(ringCentre(ring), value)
            : corner === null
              ? { refusal: 'Select a corner first.' }
              : tool === 'round'
                ? roundCorner(ring, corner, value)
                : curveEdge(ring, corner, value)

        if ('refusal' in result) {
          store().refuse(result.refusal)
          return false
        }
        return controller.replaceRing(session.activeRing, result.ring)
      },
      loadLatest,
    }

    const unregister = registerSiteBoundaryEditRuntime(runtime)
    return () => {
      unsubscribeBoundary()
      document.removeEventListener('visibilitychange', onTabVisible)
      window.removeEventListener('focus', onTabVisible)
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

  // Delete or Backspace removes the selected corner. Ignored while typing in a field, so it can never
  // eat the chat box's own Backspace.
  useEffect(() => {
    if (!inSession) return

    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null
      if (target && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))) return

      // Escape hands the map back from the Select and Draw arc tools.
      if (event.key === 'Escape' && store().session && store().session?.tool !== 'edit') {
        store().setTool('edit')
        return
      }

      if (event.key !== 'Delete' && event.key !== 'Backspace') return
      if ((store().session?.selectedCorners.length ?? 0) === 0) return

      event.preventDefault()
      siteBoundaryEditActions.deleteCorner()
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [inSession])

  // The polygons exist exactly while a session does, and are rebuilt when the session comes back
  // after leaving and returning to /studio (FR-029).
  useEffect(() => {
    if (!handle || !inSession) return

    const session = store().session
    if (!session) return

    const controller = createEditablePolygonController(createGoogleEditablePolygonHost(handle.map), {
      onVertexMenu: ({ clientX, clientY }) => useCornerMenuStore.getState().open(clientX, clientY),
    })
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
  }, [handle, inSession, sessionKey])
}
