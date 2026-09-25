import * as THREE from 'three'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ExtensionContext } from '../../viewer/extensions/context'
import { solarAnalysisExtension } from '../../viewer/extensions/builtin/solarAnalysisExtension'
import { viewerExtensionLoader } from '../../viewer/extensions/loader'
import { useViewerExtensionStore } from '../../viewer/extensions/store/viewerExtensionStore'
import { panelTypeRegistry } from '../../viewer/panels/registry'
import { useFloatingPanelStore } from '../../viewer/panels/store/floatingPanelStore'
import { useActiveLocationStore } from '../../store/activeLocationStore'
import type { DrawingSpaceHandle } from '../../viewer/scene/DrawingSpaceRegistry'
import { drawingSpaceRegistry } from '../../viewer/scene/DrawingSpaceRegistry'
import { useSolarAnalysisStore } from './store/solarAnalysisStore'
import { sceneAnchor } from '../../viewer/scene/SceneAnchor'
import type { SiteBuildingDto } from './api/siteBuildingsApi'
import { SHADOW_GATE_DEGREES, SolarScene } from './scene/SolarScene'

/**
 * T047, research D9, FR-039 — the ONE place this feature's redraw discipline is directly
 * observable: `onFrame` is registered once in `start()` and must request ZERO redraws while
 * playback is not running. This is the guard that keeps the viewer quiet when the analysis is
 * idle, since `DrawingSpaceHandle.onFrame` has no unsubscribe (research D9's documented risk).
 */
function makeFakeDrawingSpace() {
  const frameCallbacks: ((deltaSeconds: number) => void)[] = []
  const invalidate = vi.fn()
  const handle: DrawingSpaceHandle = {
    group: new THREE.Group(),
    invalidate,
    onFrame: (cb) => frameCallbacks.push(cb),
    declareDrawingRequirement: vi.fn(),
  }
  return { handle, frameCallbacks, invalidate }
}

function makeFakeContext(drawingSpace: DrawingSpaceHandle): ExtensionContext {
  return {
    engine: {} as ExtensionContext['engine'],
    on: vi.fn(),
    contributeOverlay: vi.fn(),
    contributeHudItem: vi.fn(),
    contributeToolbarEntry: vi.fn(),
    registerLivePanelKind: vi.fn(),
    openPanel: vi.fn(),
    withdrawPanel: vi.fn(),
    acquireDrawingSpace: () => drawingSpace,
  }
}

describe('solarAnalysisExtension — guarded onFrame (research D9, FR-039)', () => {
  beforeEach(() => {
    useSolarAnalysisStore.setState({ site: null, moment: null, status: 'idle', failureReason: null, buildingsNotice: null })
  })

  it('requests zero redraws when playback is not running', () => {
    const { handle, frameCallbacks, invalidate } = makeFakeDrawingSpace()
    const context = makeFakeContext(handle)

    solarAnalysisExtension.start(context)
    expect(frameCallbacks).toHaveLength(1)

    // No moment at all yet (idle) -> definitely not playing.
    frameCallbacks[0](0.016)
    expect(invalidate).not.toHaveBeenCalled()

    solarAnalysisExtension.stop()
  })

  it('requests zero redraws even once a moment exists but is not playing', async () => {
    const { handle, frameCallbacks, invalidate } = makeFakeDrawingSpace()
    const context = makeFakeContext(handle)

    solarAnalysisExtension.start(context)
    await useSolarAnalysisStore.getState().open(25.2, 55.3)
    expect(useSolarAnalysisStore.getState().moment!.isPlaying).toBe(false)

    frameCallbacks[0](0.016)
    expect(invalidate).not.toHaveBeenCalled()

    solarAnalysisExtension.stop()
  })

  it('advances the clock and invalidates only while playing', async () => {
    const { handle, frameCallbacks, invalidate } = makeFakeDrawingSpace()
    const context = makeFakeContext(handle)

    solarAnalysisExtension.start(context)
    await useSolarAnalysisStore.getState().open(25.2, 55.3)
    useSolarAnalysisStore.getState().setInstantUtc(new Date(Date.UTC(2026, 0, 1, 0, 0)))
    useSolarAnalysisStore.getState().setPlaying(true)

    frameCallbacks[0](1) // 1 real second

    expect(invalidate).toHaveBeenCalledTimes(1)
    expect(useSolarAnalysisStore.getState().moment!.instantUtc.getTime()).toBeGreaterThan(Date.UTC(2026, 0, 1, 0, 0))

    solarAnalysisExtension.stop()
  })
})

/**
 * T069, SC-006, SC-007, FR-040 — mirrors specs/051's own fifty-cycle assertion
 * (`loader.test.ts` "fifty start/stop cycles ... accumulate nothing"). Uses the REAL
 * `viewerExtensionLoader`/`drawingSpaceRegistry`/`panelTypeRegistry` singletons — not a fake
 * context — because what is actually being proven is that the FRAMEWORK's own teardown handles
 * this extension's contributions without it doing anything beyond stopping playback (contracts/
 * solar-extension.md: "`stop()` does no teardown of its own beyond stopping playback").
 */
describe('solarAnalysisExtension — fifty activate/deactivate/stop cycles accumulate nothing (SC-006, SC-007)', () => {
  it('returns scene child count, contributions and panel kinds to baseline after 50 cycles', async () => {
    const scene = new THREE.Scene()
    drawingSpaceRegistry.bind(scene)
    const baselineChildCount = scene.children.length

    for (let cycle = 0; cycle < 50; cycle++) {
      await viewerExtensionLoader.start(solarAnalysisExtension.id)
      viewerExtensionLoader.activate(solarAnalysisExtension.id)
      viewerExtensionLoader.deactivate(solarAnalysisExtension.id)
      await viewerExtensionLoader.stop(solarAnalysisExtension.id)
    }

    expect(scene.children.length).toBe(baselineChildCount)
    expect(
      useViewerExtensionStore.getState().contributions.filter((c) => c.extensionId === solarAnalysisExtension.id),
    ).toHaveLength(0)
    expect(panelTypeRegistry.resolve('solar.time-control')).toBeUndefined()
    expect(panelTypeRegistry.resolve('solar.corrections')).toBeUndefined()
  })
})

/**
 * FR-042, feedback 2026-09-25 — the analysis belongs to the site it was opened for. Uses the real
 * loader and stores, since what matters is the ordering between a location update and Lucy's own
 * `activate()` for the new site, which only the real synchronous subscription exhibits.
 */
describe('solarAnalysisExtension — leaving the site closes the analysis (FR-042)', () => {
  const id = solarAnalysisExtension.id
  const activation = () => useViewerExtensionStore.getState().extensions[id]?.activation
  const openPanelIds = () => useFloatingPanelStore.getState().panels.map((p) => p.id)

  beforeEach(async () => {
    drawingSpaceRegistry.bind(new THREE.Scene())
    useActiveLocationStore.getState().clear()
    useFloatingPanelStore.setState({ panels: [], closedPanels: [] })
    await viewerExtensionLoader.start(id)
    return async () => {
      await viewerExtensionLoader.stop(id)
      useActiveLocationStore.getState().clear()
    }
  })

  it('closes the analysis and withdraws its panels when the user moves to another site', () => {
    useActiveLocationStore.getState().setFromAgent(25.1556, 55.2216, 'Al Safa Park 2', 0.9)
    viewerExtensionLoader.activate(id)
    expect(openPanelIds()).toEqual(expect.arrayContaining(['solar-time-control', 'solar-corrections']))

    useActiveLocationStore.getState().setFromAgent(25.1972, 55.2796, 'Dubai Mall', 0.9)

    expect(activation()).toBe('inactive')
    expect(openPanelIds()).not.toContain('solar-time-control')
    expect(openPanelIds()).not.toContain('solar-corrections')
    // Withdrawn, not closed by the user — nothing to bring back from the reopen tray.
    expect(useFloatingPanelStore.getState().closedPanels).toHaveLength(0)
  })

  it('stays open when Lucy confirms a new site and opens the analysis for it in the same turn', () => {
    useActiveLocationStore.getState().setFromAgent(25.1556, 55.2216, 'Al Safa Park 2', 0.9)
    viewerExtensionLoader.activate(id)

    // The stream's order: the location event, then the solarAnalysis event's activate().
    useActiveLocationStore.getState().setFromAgent(25.1972, 55.2796, 'Dubai Mall', 0.9)
    viewerExtensionLoader.activate(id)

    expect(activation()).toBe('active')
  })

  it('keeps an analysis opened before any site existed, so it can follow the first one', () => {
    viewerExtensionLoader.activate(id)

    useActiveLocationStore.getState().setFromAgent(25.1556, 55.2216, 'Al Safa Park 2', 0.9)

    expect(activation()).toBe('active')
  })

  it('stops listening once the extension stops', async () => {
    useActiveLocationStore.getState().setFromAgent(25.1556, 55.2216, 'Al Safa Park 2', 0.9)
    await viewerExtensionLoader.stop(id)
    const deactivate = vi.spyOn(solarAnalysisExtension, 'deactivate')

    useActiveLocationStore.getState().setFromAgent(25.1972, 55.2796, 'Dubai Mall', 0.9)

    expect(deactivate).not.toHaveBeenCalled()
    deactivate.mockRestore()
  })
})


/**
 * T030, T035, T036, FR-018, FR-019, FR-020, FR-024 — the playback gate.
 *
 * `aimSun` returns whether the sun moved enough to be worth a frame; the overlay withholds its
 * `invalidate()` when it returns false, and the viewer's on-demand renderer therefore never runs a
 * frame — and never runs the shadow-map pass — for that tick. That return value IS the gate, so it
 * is what these tests assert. Nothing in this feature touches `renderer.shadowMap.autoUpdate` or
 * any other renderer-global flag, which FR-024 forbids it from owning.
 *
 * Altitude, not azimuth, is varied to size the movements: for two directions sharing an azimuth,
 * the angle between them is exactly the altitude difference, so "0.2° of sun movement" needs no
 * trigonometry to set up and the threshold is tested where it actually sits.
 */
function gateTestBuilding(id: string, heightMetres: number): SiteBuildingDto {
  const size = 0.0003
  return {
    id,
    ring: [
      { latitude: 25.1556, longitude: 55.2216 },
      { latitude: 25.1556, longitude: 55.2216 + size },
      { latitude: 25.1556 - size, longitude: 55.2216 + size },
      { latitude: 25.1556 - size, longitude: 55.2216 },
    ],
    heightMetres,
    heightProvenance: 'assumed',
    name: id,
    isSiteBuilding: true,
  }
}

describe('Shadow recomputation gate during playback (T030, FR-018, FR-019, FR-020)', () => {
  beforeEach(() => {
    sceneAnchor.set({ latitude: 25.1555, longitude: 55.2215 })
  })

  function sceneWithBuildings(heightMetres = 9): SolarScene {
    const { handle } = makeFakeDrawingSpace()
    const scene = new SolarScene(handle)
    scene.rebuildBuildings([gateTestBuilding('osm_way_1', heightMetres)])
    return scene
  }

  it('skips recomputation for sun movement below 0.25°, and allows it at or above', () => {
    expect(SHADOW_GATE_DEGREES).toBe(0.25)
    const scene = sceneWithBuildings()

    // The first update can never be gated — there is no previously applied direction to measure
    // against, and something has to be drawn.
    expect(scene.aimSun(180, 45, true)).toBe(true)

    expect(scene.aimSun(180, 45.1, true)).toBe(false) // 0.1°
    expect(scene.aimSun(180, 45.2, true)).toBe(false) // 0.2°, still measured from 45
    expect(scene.aimSun(180, 45.3, true)).toBe(true) // 0.3° — over the threshold, so it is drawn

    // ...and the next comparison is against 45.3, the direction actually applied, never against
    // the ticks that were skipped. Otherwise the skipped movement would accumulate unbounded.
    expect(scene.aimSun(180, 45.4, true)).toBe(false)
    expect(scene.aimSun(180, 45.6, true)).toBe(true)

    scene.disposeAll()
  })

  it('never gates a scrub or a single-step change, however small (FR-020)', () => {
    const scene = sceneWithBuildings()
    expect(scene.aimSun(180, 45, false)).toBe(true)

    // The same 0.1° step that was skipped during playback above. A deliberate user action must
    // always produce a frame, or the control feels broken rather than smooth.
    expect(scene.aimSun(180, 45.1, false)).toBe(true)
    expect(scene.aimSun(180, 45.1, false)).toBe(true) // even zero movement

    scene.disposeAll()
  })

  it('clears the gate immediately on a height correction (FR-019)', () => {
    const scene = sceneWithBuildings(9)
    expect(scene.aimSun(180, 45, true)).toBe(true)
    expect(scene.aimSun(180, 45.05, true)).toBe(false)

    scene.rebuildBuildings([gateTestBuilding('osm_way_1', 120)])

    // The sun has moved 0.05° — far below the threshold — but the geometry changed, so the very
    // next update must go through or the shadow would keep the old height until the sun caught up.
    expect(scene.aimSun(180, 45.1, true)).toBe(true)

    scene.disposeAll()
  })

  it('clears the gate immediately on a ground-offset change (FR-019)', () => {
    const scene = sceneWithBuildings()
    expect(scene.aimSun(180, 45, true)).toBe(true)
    expect(scene.aimSun(180, 45.05, true)).toBe(false)

    scene.setGroundOffset(3)
    expect(scene.aimSun(180, 45.1, true)).toBe(true)

    scene.disposeAll()
  })

  it('clears the gate immediately when new building data arrives (FR-019)', () => {
    const { handle } = makeFakeDrawingSpace()
    const scene = new SolarScene(handle)
    scene.rebuildBuildings([]) // nothing fetched yet
    expect(scene.aimSun(180, 45, true)).toBe(true)
    expect(scene.aimSun(180, 45.05, true)).toBe(false)

    scene.rebuildBuildings([gateTestBuilding('osm_way_1', 30), gateTestBuilding('osm_way_2', 18)])
    expect(scene.aimSun(180, 45.1, true)).toBe(true)

    scene.disposeAll()
  })
})
