import { act, cleanup, render, waitFor } from '@testing-library/react'
import * as THREE from 'three'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import type { ExtensionContext } from '../../../viewer/extensions/context'
import { useViewerExtensionStore } from '../../../viewer/extensions/store/viewerExtensionStore'
import { drawingSpaceRegistry } from '../../../viewer/scene/DrawingSpaceRegistry'
import { sceneAnchor } from '../../../viewer/scene/SceneAnchor'
import * as siteBuildingsApi from '../api/siteBuildingsApi'
import { copy } from '../copy'
import { EXTENSION_ID, makeSolarAnalysisOverlay, requestSolarAnalysisMoment } from './SolarAnalysisOverlay'
import { SolarScene } from '../scene/SolarScene'
import { useSolarAnalysisStore } from '../store/solarAnalysisStore'

vi.mock('../api/siteBuildingsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/siteBuildingsApi')>()
  return {
    ...actual,
    getSiteBuildings: vi.fn().mockResolvedValue({ buildings: [], limited: false, excludedCount: 0, radiusMetres: 200 }),
  }
})

function makeFakeContext(): ExtensionContext {
  return {
    engine: {} as ExtensionContext['engine'],
    on: vi.fn(),
    contributeOverlay: vi.fn(),
    contributeHudItem: vi.fn(),
    contributeToolbarEntry: vi.fn(),
    registerLivePanelKind: vi.fn(),
    openPanel: vi.fn(),
    withdrawPanel: vi.fn(),
    acquireDrawingSpace: vi.fn(),
  }
}

afterEach(() => {
  cleanup()
})

function setActivation(active: boolean) {
  useViewerExtensionStore.setState({
    extensions: { [EXTENSION_ID]: { lifecycle: 'started', failureReason: null, activation: active ? 'active' : 'inactive', lastEventError: null } },
    contributions: [],
  })
}

describe('SolarAnalysisOverlay (US1 scenarios 1-6, FR-008, FR-042, research D10)', () => {
  beforeEach(() => {
    useActiveLocationStore.setState({
      source: null,
      latitude: null,
      longitude: null,
      locationName: null,
      confidence: null,
      locationType: null,
      viewport: null,
    })
    useSolarAnalysisStore.setState({ site: null, moment: null, status: 'idle', failureReason: null, buildingsNotice: null, showBuildingMass: false })
    useViewerExtensionStore.setState({ extensions: {}, contributions: [] })
  })

  it('opens the analysis for the active site once activated (US1 scenario 1)', async () => {
    useActiveLocationStore.setState({ latitude: 25.2, longitude: 55.3 })
    const context = makeFakeContext()
    const drawingSpace = drawingSpaceRegistry.acquire('overlay-test-1')
    const sceneRef = { current: new SolarScene(drawingSpace) }
    const Overlay = makeSolarAnalysisOverlay(context, sceneRef)

    setActivation(true)
    render(<Overlay />)
    await waitFor(() => expect(useSolarAnalysisStore.getState().site).not.toBeNull())

    expect(useSolarAnalysisStore.getState().site?.latitude).toBe(25.2)
    await waitFor(() => expect(context.openPanel).toHaveBeenCalled())
    drawingSpaceRegistry.release('overlay-test-1')
  })

  it('recomputes for a new site rather than continuing against the old one (US1 scenario 6, research D10)', async () => {
    useActiveLocationStore.setState({ latitude: 25.2, longitude: 55.3 })
    const context = makeFakeContext()
    const drawingSpace = drawingSpaceRegistry.acquire('overlay-test-2')
    const sceneRef = { current: new SolarScene(drawingSpace) }
    const Overlay = makeSolarAnalysisOverlay(context, sceneRef)
    setActivation(true)

    render(<Overlay />)
    await waitFor(() => expect(useSolarAnalysisStore.getState().site?.latitude).toBe(25.2))

    act(() => {
      useActiveLocationStore.setState({ latitude: 51.5, longitude: -0.1 })
    })

    await waitFor(() => expect(useSolarAnalysisStore.getState().site?.latitude).toBe(51.5))
    drawingSpaceRegistry.release('overlay-test-2')
  })

  it('closes when the viewer has no site at all while active (FR-042)', async () => {
    useActiveLocationStore.setState({ latitude: 25.2, longitude: 55.3 })
    const context = makeFakeContext()
    const drawingSpace = drawingSpaceRegistry.acquire('overlay-test-3')
    const sceneRef = { current: new SolarScene(drawingSpace) }
    const Overlay = makeSolarAnalysisOverlay(context, sceneRef)
    setActivation(true)

    render(<Overlay />)
    await waitFor(() => expect(useSolarAnalysisStore.getState().site).not.toBeNull())

    act(() => {
      useActiveLocationStore.setState({ latitude: null, longitude: null })
    })

    await waitFor(() => expect(useSolarAnalysisStore.getState().status).toBe('idle'))
    expect(useSolarAnalysisStore.getState().site).toBeNull()
    drawingSpaceRegistry.release('overlay-test-3')
  })

  it('does nothing while inactive (activation !== "active")', async () => {
    useActiveLocationStore.setState({ latitude: 25.2, longitude: 55.3 })
    const context = makeFakeContext()
    const drawingSpace = drawingSpaceRegistry.acquire('overlay-test-4')
    const sceneRef = { current: new SolarScene(drawingSpace) }
    const Overlay = makeSolarAnalysisOverlay(context, sceneRef)
    setActivation(false)

    await act(async () => {
      render(<Overlay />)
      await Promise.resolve()
    })

    expect(useSolarAnalysisStore.getState().site).toBeNull()
    drawingSpaceRegistry.release('overlay-test-4')
  })
})

describe('SolarAnalysisOverlay — buildings fetch (T042, T043, FR-013, FR-014, FR-015, research D10)', () => {
  beforeEach(() => {
    useActiveLocationStore.setState({
      source: null, latitude: null, longitude: null, locationName: null, confidence: null, locationType: null, viewport: null,
    })
    useSolarAnalysisStore.setState({ site: null, moment: null, status: 'idle', failureReason: null, buildingsNotice: null })
    useViewerExtensionStore.setState({ extensions: {}, contributions: [] })
    vi.mocked(siteBuildingsApi.getSiteBuildings).mockReset()
  })

  it('marks the analysis partial with "no buildings found" when the endpoint returns none', async () => {
    vi.mocked(siteBuildingsApi.getSiteBuildings).mockResolvedValue({ buildings: [], limited: false, excludedCount: 0, radiusMetres: 200 })
    useActiveLocationStore.setState({ latitude: 25.2, longitude: 55.3 })
    const context = makeFakeContext()
    const drawingSpace = drawingSpaceRegistry.acquire('overlay-test-5')
    const sceneRef = { current: new SolarScene(drawingSpace) }
    setActivation(true)

    const Overlay = makeSolarAnalysisOverlay(context, sceneRef)
    render(<Overlay />)

    await waitFor(() => expect(useSolarAnalysisStore.getState().status).toBe('partial'))
    expect(useSolarAnalysisStore.getState().buildingsNotice).toBe(copy.noBuildingsFound)
    drawingSpaceRegistry.release('overlay-test-5')
  })

  it('marks the analysis partial with the unavailable notice when the endpoint rejects, and the sun path still works', async () => {
    vi.mocked(siteBuildingsApi.getSiteBuildings).mockRejectedValue(new Error('503'))
    useActiveLocationStore.setState({ latitude: 25.2, longitude: 55.3 })
    const context = makeFakeContext()
    const drawingSpace = drawingSpaceRegistry.acquire('overlay-test-6')
    const sceneRef = { current: new SolarScene(drawingSpace) }
    setActivation(true)

    const Overlay = makeSolarAnalysisOverlay(context, sceneRef)
    render(<Overlay />)

    await waitFor(() => expect(useSolarAnalysisStore.getState().status).toBe('partial'))
    expect(useSolarAnalysisStore.getState().buildingsNotice).toBe(copy.buildingDataUnavailable)
    // Sun path figures still opened despite the buildings failure.
    await waitFor(() => expect(context.openPanel).toHaveBeenCalled())
    drawingSpaceRegistry.release('overlay-test-6')
  })

  it('marks ready and rebuilds the scene buildings group when buildings are found cleanly', async () => {
    const building = {
      id: 'osm_way_1',
      ring: [{ latitude: 25.156, longitude: 55.221 }, { latitude: 25.156, longitude: 55.222 }, { latitude: 25.155, longitude: 55.222 }],
      heightMetres: 30,
      heightProvenance: 'known' as const,
      name: 'Test',
      isSiteBuilding: true,
    }
    sceneAnchor.set({ latitude: 25.2, longitude: 55.3 }) // worldToLocal needs a reference point
    vi.mocked(siteBuildingsApi.getSiteBuildings).mockResolvedValue({ buildings: [building], limited: false, excludedCount: 0, radiusMetres: 200 })
    useActiveLocationStore.setState({ latitude: 25.2, longitude: 55.3 })
    const context = makeFakeContext()
    const drawingSpace = drawingSpaceRegistry.acquire('overlay-test-7')
    const sceneRef = { current: new SolarScene(drawingSpace) }
    setActivation(true)

    const Overlay = makeSolarAnalysisOverlay(context, sceneRef)
    render(<Overlay />)

    await waitFor(() => expect(useSolarAnalysisStore.getState().status).toBe('ready'))
    expect(sceneRef.current!.buildingsGroup.children.length).toBeGreaterThan(0)
    drawingSpaceRegistry.release('overlay-test-7')
  })

  // The Building Corrections panel's massing switch: draws the shadow casters in place, no rebuild.
  it('draws the building massing while the switch is on, and hides it again when off', async () => {
    const building = {
      id: 'osm_way_1',
      ring: [{ latitude: 25.156, longitude: 55.221 }, { latitude: 25.156, longitude: 55.222 }, { latitude: 25.155, longitude: 55.222 }],
      heightMetres: 30,
      heightProvenance: 'known' as const,
      name: 'Test',
      isSiteBuilding: true,
    }
    sceneAnchor.set({ latitude: 25.2, longitude: 55.3 })
    vi.mocked(siteBuildingsApi.getSiteBuildings).mockResolvedValue({ buildings: [building], limited: false, excludedCount: 0, radiusMetres: 200 })
    useActiveLocationStore.setState({ latitude: 25.2, longitude: 55.3 })
    const drawingSpace = drawingSpaceRegistry.acquire('overlay-test-mass')
    const sceneRef = { current: new SolarScene(drawingSpace) }
    setActivation(true)

    const Overlay = makeSolarAnalysisOverlay(makeFakeContext(), sceneRef)
    render(<Overlay />)
    await waitFor(() => expect(sceneRef.current!.buildingsGroup.children.length).toBeGreaterThan(0))
    const material = () => ((sceneRef.current!.buildingsGroup.children[0] as THREE.Mesh).material as THREE.Material)
    expect(material().colorWrite).toBe(false)

    act(() => useSolarAnalysisStore.getState().setShowBuildingMass(true))
    expect(material().colorWrite).toBe(true)
    expect(material().depthWrite).toBe(true)

    act(() => useSolarAnalysisStore.getState().setShowBuildingMass(false))
    expect(material().colorWrite).toBe(false)
    drawingSpaceRegistry.release('overlay-test-mass')
  })

  it('discards a stale buildings response whose site key no longer matches the current site', async () => {
    let resolveFirst!: (value: siteBuildingsApi.SiteBuildingsResponse) => void
    const firstPromise = new Promise<siteBuildingsApi.SiteBuildingsResponse>((resolve) => { resolveFirst = resolve })
    vi.mocked(siteBuildingsApi.getSiteBuildings)
      .mockImplementationOnce(() => firstPromise)
      .mockResolvedValueOnce({ buildings: [], limited: false, excludedCount: 0, radiusMetres: 200 })

    useActiveLocationStore.setState({ latitude: 25.2, longitude: 55.3 })
    const context = makeFakeContext()
    const drawingSpace = drawingSpaceRegistry.acquire('overlay-test-8')
    const sceneRef = { current: new SolarScene(drawingSpace) }
    setActivation(true)

    const Overlay = makeSolarAnalysisOverlay(context, sceneRef)
    render(<Overlay />)
    await waitFor(() => expect(useSolarAnalysisStore.getState().site?.latitude).toBe(25.2))

    // Site changes before the first (slow) response arrives.
    act(() => {
      useActiveLocationStore.setState({ latitude: 51.5, longitude: -0.1 })
    })
    await waitFor(() => expect(useSolarAnalysisStore.getState().site?.latitude).toBe(51.5))

    // Now the stale first response resolves with buildings that must NOT be applied.
    resolveFirst({
      buildings: [{ id: 'stale', ring: [], heightMetres: 1, heightProvenance: 'known', name: '', isSiteBuilding: false }],
      limited: false, excludedCount: 0, radiusMetres: 200,
    })
    await new Promise((r) => setTimeout(r, 10))

    expect(useSolarAnalysisStore.getState().site?.latitude).toBe(51.5) // unchanged by the stale response
    drawingSpaceRegistry.release('overlay-test-8')
  })
})

/** contracts/open-solar-analysis-capability.md — found in review of specs/052: a date/time Lucy
 * asked for was dropped whenever the analysis was already open on the current site, because
 * `useChatStream` calls `activate()` (idempotent) right after requesting it, so neither the
 * activation state nor the active location changes and the follow-site effect never re-runs. The
 * request also stayed pending, so it could later fire against an unrelated site. */
describe('requestSolarAnalysisMoment (FR-033, Lucy asking for a specific moment)', () => {
  beforeEach(async () => {
    sceneAnchor.set({ latitude: 25.2, longitude: 55.27 })
    useSolarAnalysisStore.setState({ site: null, moment: null, status: 'idle' })
  })

  it('applies the requested date and time when the analysis is ALREADY open on this site', async () => {
    await act(async () => {
      await useSolarAnalysisStore.getState().open(25.2, 55.27)
    })
    expect(useSolarAnalysisStore.getState().site).not.toBeNull()

    act(() => {
      requestSolarAnalysisMoment('2026-06-21', '15:30')
    })

    const moment = useSolarAnalysisStore.getState().moment!
    expect(moment.localDate).toBe('2026-06-21')
    expect(moment.localMinuteOfDay).toBe(15 * 60 + 30)
  })

  it('consumes the request, so a later site change cannot re-apply it over the user own edit', async () => {
    await act(async () => {
      await useSolarAnalysisStore.getState().open(25.2, 55.27)
    })
    act(() => {
      requestSolarAnalysisMoment('2026-06-21', '15:30')
    })

    // The user then picks their own date, and afterwards moves to a different site.
    act(() => {
      useSolarAnalysisStore.getState().setLocalDate('2026-12-01')
    })
    await act(async () => {
      await useSolarAnalysisStore.getState().followSite(51.5, -0.12)
    })

    // Lucy's already-consumed request must not resurface and overwrite the user's own choice.
    expect(useSolarAnalysisStore.getState().moment!.localDate).toBe('2026-12-01')
  })
})


/**
 * T030, T035, T036, FR-018, FR-020, FR-024 — the gate's end: the overlay has to actually WITHHOLD
 * `invalidate()` on a gated tick. `SolarScene.aimSun` only decides; if the overlay invalidated
 * anyway the viewer would draw the frame regardless and the gate would buy nothing. Asserted here,
 * against the real wiring, rather than by re-deriving the overlay's own condition in a unit test.
 */
describe('SolarAnalysisOverlay withholds the redraw on a gated playback tick (T030, FR-018)', () => {
  beforeEach(() => {
    useActiveLocationStore.setState({
      source: null,
      latitude: null,
      longitude: null,
      locationName: null,
      confidence: null,
      locationType: null,
      viewport: null,
    })
    useSolarAnalysisStore.setState({ site: null, moment: null, status: 'idle', failureReason: null, buildingsNotice: null })
    useViewerExtensionStore.setState({ extensions: {}, contributions: [] })
    // Re-armed here: an earlier suite in this file leaves one-shot rejections queued on the mock.
    vi.mocked(siteBuildingsApi.getSiteBuildings).mockReset()
    vi.mocked(siteBuildingsApi.getSiteBuildings).mockResolvedValue({ buildings: [], limited: false, excludedCount: 0, radiusMetres: 200 })
  })

  it('skips invalidate for a sub-threshold tick while playing, and issues it for a real move', async () => {
    useActiveLocationStore.setState({ latitude: 25.2, longitude: 55.3 })
    const context = makeFakeContext()
    const drawingSpace = drawingSpaceRegistry.acquire('overlay-gate-test')
    const solarScene = new SolarScene(drawingSpace)
    const Overlay = makeSolarAnalysisOverlay(context, { current: solarScene })

    setActivation(true)
    render(<Overlay />)
    await waitFor(() => expect(useSolarAnalysisStore.getState().site).not.toBeNull())

    const noon = new Date(Date.UTC(2026, 8, 22, 8, 0)) // midday at Dubai
    act(() => {
      useSolarAnalysisStore.getState().setInstantUtc(noon)
      useSolarAnalysisStore.getState().setPlaying(true)
    })
    await waitFor(() => expect(useSolarAnalysisStore.getState().moment!.isPlaying).toBe(true))

    const invalidate = vi.spyOn(solarScene, 'invalidate')

    // Ten seconds of solar time — well under 0.25° of movement, and under a pixel of shadow edge.
    act(() => {
      useSolarAnalysisStore.getState().setInstantUtc(new Date(noon.getTime() + 10_000))
    })
    await waitFor(() => expect(context.openPanel).toHaveBeenCalled()) // figures still keep pace
    expect(invalidate).not.toHaveBeenCalled()

    // Half an hour later the sun has moved several degrees, so the frame must be drawn.
    act(() => {
      useSolarAnalysisStore.getState().setInstantUtc(new Date(noon.getTime() + 30 * 60_000))
    })
    await waitFor(() => expect(invalidate).toHaveBeenCalled())

    invalidate.mockRestore()
    drawingSpaceRegistry.release('overlay-gate-test')
  })
})
