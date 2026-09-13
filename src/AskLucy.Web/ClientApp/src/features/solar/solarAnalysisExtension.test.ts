import * as THREE from 'three'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ExtensionContext } from '../../viewer/extensions/context'
import { solarAnalysisExtension } from '../../viewer/extensions/builtin/solarAnalysisExtension'
import { viewerExtensionLoader } from '../../viewer/extensions/loader'
import { useViewerExtensionStore } from '../../viewer/extensions/store/viewerExtensionStore'
import { panelTypeRegistry } from '../../viewer/panels/registry'
import type { DrawingSpaceHandle } from '../../viewer/scene/DrawingSpaceRegistry'
import { drawingSpaceRegistry } from '../../viewer/scene/DrawingSpaceRegistry'
import { useSolarAnalysisStore } from './store/solarAnalysisStore'

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
    contributeToolbarEntry: vi.fn(),
    registerLivePanelKind: vi.fn(),
    openPanel: vi.fn(),
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
