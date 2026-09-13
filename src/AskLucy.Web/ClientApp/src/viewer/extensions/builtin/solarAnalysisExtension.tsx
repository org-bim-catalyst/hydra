import { RiSunLine } from '@remixicon/react'
import { makeCameraAttitudeWidget } from '../../../features/solar/components/CameraAttitudeWidget'
import { EXTENSION_ID, makeSolarAnalysisOverlay } from '../../../features/solar/components/SolarAnalysisOverlay'
import { copy } from '../../../features/solar/copy'
import {
  BuildingCorrectionsPanel,
  SOLAR_CORRECTIONS_TYPE_KEY,
  solarCorrectionsDataSchema,
} from '../../../features/solar/panels/BuildingCorrectionsPanel'
import {
  SOLAR_TIME_CONTROL_TYPE_KEY,
  SolarTimeControlPanel,
  solarTimeControlDataSchema,
} from '../../../features/solar/panels/SolarTimeControlPanel'
import { SolarScene } from '../../../features/solar/scene/SolarScene'
import { useSolarAnalysisStore } from '../../../features/solar/store/solarAnalysisStore'
import { DEFAULT_CONTENT_CHROME } from '../../panels/chrome/chrome'
import type { ExtensionContext } from '../context'
import { viewerExtensionLoader } from '../loader'
import { viewerExtensionRegistry } from '../registry'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'
import type { ViewerExtension } from '../ViewerExtension'

/**
 * specs/052-solar-analysis — sun path, real building shadows and time scrubbing over the active
 * site, built entirely as a viewer extension on the frameworks specs/049-051 shipped
 * (contracts/solar-extension.md). This file is a thin registration shim: it reaches the viewer
 * only through `ExtensionContext` (FR-037) and holds no reference to the shared `THREE.Scene`,
 * `Camera` or `WebGLRenderer` — everything the analysis actually draws lives in
 * `features/solar/scene/SolarScene.ts` and is reached only through the acquired drawing space.
 *
 * `toggleable: true` / `startsWithViewer: false` — the extension starts with every other declared
 * extension (acquiring its drawing space and declaring `toneMapping`), but the analysis itself is
 * opened only by the user (toolbar, FR-029) or by Lucy (FR-033), via activate/deactivate.
 *
 * `shadows` is declared only in User Story 2 (T044) — declaring it flips a global renderer
 * setting, which research D6 requires be its own deliberately reviewed step, not a side effect of
 * this shell existing.
 */
const sceneRef: { current: SolarScene | null } = { current: null }
let savedContext: ExtensionContext | null = null
const TIME_CONTROL_PANEL_REQUEST_ID = 'solar-time-control'
const CORRECTIONS_PANEL_REQUEST_ID = 'solar-corrections'

function toggleActivation(): void {
  const activation = useViewerExtensionStore.getState().extensions[EXTENSION_ID]?.activation
  if (activation === 'active') {
    viewerExtensionLoader.deactivate(EXTENSION_ID)
  } else {
    viewerExtensionLoader.activate(EXTENSION_ID)
  }
}

export const solarAnalysisExtension: ViewerExtension = {
  id: EXTENSION_ID,
  manifest: {
    displayName: 'Solar Analysis',
    description: 'Sun path, shadows and time of day over the site.',
    toggleable: true,
    startsWithViewer: false,
  },

  start(context: ExtensionContext) {
    savedContext = context
    const drawingSpace = context.acquireDrawingSpace()
    drawingSpace.declareDrawingRequirement('toneMapping')
    // FR-016, research D6 — declared (never `renderer.shadowMap.enabled` set directly), and
    // deliberately reviewed as its own step rather than a side effect of adding a light: this is
    // the ONE renderer-global change this feature introduces (toneMapping above was already the
    // considered default per specs/051's own research D5, so it changes nothing new). A live
    // visual A/B of the magma-glow and boundary-highlight layers (tuned without shadows) was not
    // measured in this headless environment — see the feature README for the human method.
    drawingSpace.declareDrawingRequirement('shadows')
    sceneRef.current = new SolarScene(drawingSpace)
    sceneRef.current.drawingSpace.group.visible = false

    // FR-029 — reached without Lucy, through the viewer's own toolbar.
    context.contributeToolbarEntry({
      id: `${EXTENSION_ID}-toolbar-entry`,
      label: copy.toolbarLabel,
      icon: RiSunLine,
      onClick: toggleActivation,
    })

    // FR-008, FR-031, FR-042 — site following and figures-panel refresh (T021).
    context.contributeOverlay(makeSolarAnalysisOverlay(context, sceneRef))

    // True north and camera tilt. Kept out of the figures panel deliberately: the figures track
    // the time of day, these two track the camera, so they answer different questions and respond
    // to different inputs. Withdrawn by the framework with every other contribution on stop.
    context.contributeOverlay(makeCameraAttitudeWidget(context))

    // FR-030 — the time control is a live panel (interactive code with its own state, research
    // D12), registered once at start so it is ready the moment the user activates the analysis.
    context.registerLivePanelKind({
      typeKey: SOLAR_TIME_CONTROL_TYPE_KEY,
      renderer: SolarTimeControlPanel,
      schema: solarTimeControlDataSchema,
      chrome: { ...DEFAULT_CONTENT_CHROME, defaultSize: { width: 420, height: 160 } },
    })

    // FR-030 — building corrections are likewise interactive code with their own state.
    context.registerLivePanelKind({
      typeKey: SOLAR_CORRECTIONS_TYPE_KEY,
      renderer: BuildingCorrectionsPanel,
      schema: solarCorrectionsDataSchema,
      chrome: { ...DEFAULT_CONTENT_CHROME, defaultSize: { width: 360, height: 360 } },
    })

    // research D9, FR-039 — registered ONCE. Inert unless playing, and critically, requests no
    // redraw when inert: `advanceBy` (and therefore `invalidate()`) is called only while playback
    // is running, which is what keeps the viewer quiet when the analysis is idle.
    drawingSpace.onFrame((deltaSeconds) => {
      const store = useSolarAnalysisStore.getState()
      if (!store.moment?.isPlaying) return
      store.advanceBy(deltaSeconds)
      drawingSpace.invalidate()
    })
  },

  activate() {
    if (!sceneRef.current) return
    sceneRef.current.drawingSpace.group.visible = true
    savedContext?.openPanel({
      kind: 'live',
      requestId: TIME_CONTROL_PANEL_REQUEST_ID,
      title: copy.timeControlPanelTitle,
      typeKey: SOLAR_TIME_CONTROL_TYPE_KEY,
      data: {},
    })
    savedContext?.openPanel({
      kind: 'live',
      requestId: CORRECTIONS_PANEL_REQUEST_ID,
      title: copy.correctionsPanelTitle,
      typeKey: SOLAR_CORRECTIONS_TYPE_KEY,
      data: {},
    })
  },

  deactivate() {
    useSolarAnalysisStore.getState().setPlaying(false)
    useSolarAnalysisStore.getState().close()
    if (sceneRef.current) sceneRef.current.drawingSpace.group.visible = false
  },

  stop() {
    // All teardown beyond stopping playback is framework-owned (FR-040, contracts/solar-
    // extension.md): the drawing space, its declared requirements, contributed panels, the
    // toolbar entry, the overlay and every event subscription are withdrawn by the loader, not by
    // this file.
    useSolarAnalysisStore.getState().setPlaying(false)
    useSolarAnalysisStore.getState().close()
    sceneRef.current = null
    savedContext = null
  },
}

viewerExtensionRegistry.register(solarAnalysisExtension)
