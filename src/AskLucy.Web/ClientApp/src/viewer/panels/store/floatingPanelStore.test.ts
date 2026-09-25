import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as THREE from 'three'
import { z } from 'zod'
import { viewerEngine } from '../../engine/viewerEngineInstance'
import { sceneAnchor } from '../../scene/SceneAnchor'
import { panelTypeRegistry } from '../registry'
import { MAX_CONCURRENT_PANELS } from '../types/panel'
import { useFloatingPanelStore } from './floatingPanelStore'

const { loadGltfContentMock } = vi.hoisted(() => ({ loadGltfContentMock: vi.fn() }))
vi.mock('../../content/loaders/gltfContentLoader', () => ({ loadGltfContent: loadGltfContentMock }))

const TEST_TYPE_KEY = `test-panel-${Math.random()}`

panelTypeRegistry.register({
  typeKey: TEST_TYPE_KEY,
  renderer: () => null,
  schema: z.object({ label: z.string() }),
  chrome: { titleBar: true, resizable: true, defaultSize: { width: 320, height: 240 } },
})

const initialState = useFloatingPanelStore.getState()

describe('floatingPanelStore.openPanel validation states (live panels)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('marks a panel valid when the type is registered and data matches its schema', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'p1', typeKey: TEST_TYPE_KEY, title: 'Test', data: { label: 'ok' } })
    const panel = useFloatingPanelStore.getState().panels[0]
    expect(panel.validationStatus).toBe('valid')
    expect(panel.data).toEqual({ label: 'ok' })
  })

  it('marks a panel unknown-type when the typeKey has no registered definition', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'p2', typeKey: 'does-not-exist', title: 'Test', data: {} })
    const panel = useFloatingPanelStore.getState().panels[0]
    expect(panel.validationStatus).toBe('unknown-type')
  })

  it('marks a panel invalid when data fails the resolved schema, with a validationError set', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'p3', typeKey: TEST_TYPE_KEY, title: 'Test', data: { nonsense: true } })
    const panel = useFloatingPanelStore.getState().panels[0]
    expect(panel.validationStatus).toBe('invalid')
    expect(panel.validationError).toBeTruthy()
  })
})

describe('floatingPanelStore.openPanel validation states (content panels, specs/049)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('marks a panel valid when content matches the vocabulary', () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'content',
      requestId: 'c1',
      title: 'Test',
      content: { version: 1, blocks: [{ kind: 'heading', text: 'Hello' }] },
    })
    const panel = useFloatingPanelStore.getState().panels[0]
    expect(panel.validationStatus).toBe('valid')
    expect(panel.content).toEqual({ version: 1, blocks: [{ kind: 'heading', text: 'Hello' }] })
  })

  it('marks a panel invalid when content fails the vocabulary schema, with a validationError set', () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'content',
      requestId: 'c2',
      title: 'Test',
      content: { version: 1, blocks: [] },
    })
    const panel = useFloatingPanelStore.getState().panels[0]
    expect(panel.validationStatus).toBe('invalid')
    expect(panel.validationError).toBeTruthy()
  })
})

describe('floatingPanelStore cascade placement (FR-021)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('offsets each new panel with no position from the last', () => {
    const store = useFloatingPanelStore.getState()
    store.openPanel({ kind: 'live', requestId: 'c1', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    store.openPanel({ kind: 'live', requestId: 'c2', typeKey: TEST_TYPE_KEY, title: 'B', data: { label: 'b' } })
    const [first, second] = useFloatingPanelStore.getState().panels
    expect(second.position.x).toBeGreaterThan(first.position.x)
    expect(second.position.y).toBeGreaterThan(first.position.y)
  })

  it('wraps back toward the starting corner after enough panels', () => {
    const store = useFloatingPanelStore.getState()
    for (let i = 0; i < 10; i += 1) {
      store.openPanel({ kind: 'live', requestId: `wrap-${i}`, typeKey: TEST_TYPE_KEY, title: 'W', data: { label: 'w' } })
    }
    // Focus wrap-0 so it isn't the least-recently-focused panel once the cap-triggering 11th
    // panel is opened below (MAX_CONCURRENT_PANELS is also 10) — this test is about the cascade
    // offset math wrapping, not eviction (covered separately), so keep wrap-0 alive to compare.
    store.focusPanel('wrap-0')
    store.openPanel({ kind: 'live', requestId: 'wrap-10', typeKey: TEST_TYPE_KEY, title: 'W', data: { label: 'w' } })

    const panels = useFloatingPanelStore.getState().panels
    const first = panels.find((p) => p.id === 'wrap-0')!
    const eleventh = panels.find((p) => p.id === 'wrap-10')!
    expect(eleventh.position).toEqual(first.position)
  })

  it('does not consume a cascade slot when a position is explicitly supplied', () => {
    const store = useFloatingPanelStore.getState()
    store.openPanel({
      kind: 'live',
      requestId: 'explicit',
      typeKey: TEST_TYPE_KEY,
      title: 'Explicit',
      data: { label: 'e' },
      position: { x: 999, y: 999 },
    })
    store.openPanel({ kind: 'live', requestId: 'cascaded', typeKey: TEST_TYPE_KEY, title: 'Cascaded', data: { label: 'c' } })
    const cascaded = useFloatingPanelStore.getState().panels.find((p) => p.id === 'cascaded')!
    expect(cascaded.position).toEqual({ x: 40, y: 40 })
  })
})

describe('floatingPanelStore LRU eviction at MAX_CONCURRENT_PANELS (FR-022)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('evicts the least-recently-focused panel when a new panel would exceed the cap', () => {
    const store = useFloatingPanelStore.getState();
    for (let i = 0; i < MAX_CONCURRENT_PANELS; i += 1) {
      store.openPanel({ kind: 'live', requestId: `cap-${i}`, typeKey: TEST_TYPE_KEY, title: 'Cap', data: { label: 'cap' } })
    }
    expect(useFloatingPanelStore.getState().panels).toHaveLength(MAX_CONCURRENT_PANELS)

    store.openPanel({ kind: 'live', requestId: 'cap-overflow', typeKey: TEST_TYPE_KEY, title: 'Overflow', data: { label: 'o' } })

    const panels = useFloatingPanelStore.getState().panels
    expect(panels).toHaveLength(MAX_CONCURRENT_PANELS)
    expect(panels.some((p) => p.id === 'cap-0')).toBe(false)
    expect(panels.some((p) => p.id === 'cap-overflow')).toBe(true)
  })

  it('never blocks the request that would exceed the cap — it always succeeds', () => {
    const store = useFloatingPanelStore.getState()
    for (let i = 0; i < MAX_CONCURRENT_PANELS + 1; i += 1) {
      store.openPanel({ kind: 'live', requestId: `never-blocked-${i}`, typeKey: TEST_TYPE_KEY, title: 'X', data: { label: 'x' } })
    }
    expect(useFloatingPanelStore.getState().panels.some((p) => p.id === `never-blocked-${MAX_CONCURRENT_PANELS}`)).toBe(
      true,
    )
  })
})

describe('floatingPanelStore.clampToViewport (FR-018, Edge Cases: viewport resize)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('repositions a panel back within bounds when the viewport shrinks under it', () => {
    const store = useFloatingPanelStore.getState()
    store.openPanel({
      kind: 'live',
      requestId: 'out-of-bounds',
      typeKey: TEST_TYPE_KEY,
      title: 'Out of bounds',
      data: { label: 'x' },
      position: { x: 900, y: 700 },
    })
    useFloatingPanelStore.setState((s) => ({
      panels: s.panels.map((p) => (p.id === 'out-of-bounds' ? { ...p, size: { width: 320, height: 240 } } : p)),
    }))

    store.clampToViewport({ width: 800, height: 600 })

    const panel = useFloatingPanelStore.getState().panels.find((p) => p.id === 'out-of-bounds')!
    expect(panel.position.x).toBeLessThanOrEqual(800 - 320)
    expect(panel.position.y).toBeLessThanOrEqual(600 - 240)
  })

  it('leaves an already-in-bounds panel untouched', () => {
    const store = useFloatingPanelStore.getState()
    store.openPanel({
      kind: 'live',
      requestId: 'in-bounds',
      typeKey: TEST_TYPE_KEY,
      title: 'In bounds',
      data: { label: 'x' },
      position: { x: 40, y: 40 },
    })

    store.clampToViewport({ width: 1200, height: 900 })

    const panel = useFloatingPanelStore.getState().panels.find((p) => p.id === 'in-bounds')!
    expect(panel.position).toEqual({ x: 40, y: 40 })
  })
})

describe('floatingPanelStore ViewerEventBus subscription (US4, FR-014, Edge Cases: removed viewer object)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it("marks a panel's association invalid when its associated layer is removed", () => {
    viewerEngine.addLayer({ id: 'ctx-layer-1', kind: 'model' })
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'ctx-panel-1',
      typeKey: TEST_TYPE_KEY,
      title: 'Ctx',
      data: { label: 'x' },
      contextAssociation: { layerId: 'ctx-layer-1' },
    })
    expect(useFloatingPanelStore.getState().panels[0].contextStatus).toBe('current')

    viewerEngine.removeLayer('ctx-layer-1')

    expect(useFloatingPanelStore.getState().panels[0].contextStatus).toBe('invalid')
  })

  it("marks a panel's association stale when its associated layer's content reloads", () => {
    viewerEngine.addLayer({ id: 'ctx-layer-2', kind: 'model' })
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'ctx-panel-2',
      typeKey: TEST_TYPE_KEY,
      title: 'Ctx',
      data: { label: 'x' },
      contextAssociation: { layerId: 'ctx-layer-2' },
    })

    viewerEngine.displayContent('ctx-layer-2', { some: 'update' })

    expect(useFloatingPanelStore.getState().panels[0].contextStatus).toBe('stale')
  })

  it('T031a: also marks stale when contentLoaded is fired via the new loadContent/replaceContent path (specs/051), not only the pre-existing displayContent path', async () => {
    sceneAnchor.set({ latitude: 25.2, longitude: 55.27 })
    let resolveLoad: (value: { ok: true; result: { root: THREE.Object3D; elementIndex: Map<string, Record<string, unknown>> } }) => void
    loadGltfContentMock.mockReturnValue(new Promise((resolve) => { resolveLoad = resolve }))

    const { contentId } = viewerEngine.loadContent(
      { kind: 'model', format: 'gltf', fileId: 'file-1' },
      { latitude: 25.2, longitude: 55.27, heightMetres: 0, orientationDegrees: 0, scale: 1 },
    ).data!
    const layerId = viewerEngine.listContent().data!.content.find((c) => c.id === contentId)!.layerId

    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'ctx-panel-content-api',
      typeKey: TEST_TYPE_KEY,
      title: 'Ctx',
      data: { label: 'x' },
      contextAssociation: { layerId },
    })
    expect(useFloatingPanelStore.getState().panels[0].contextStatus).toBe('current')

    resolveLoad!({ ok: true, result: { root: new THREE.Object3D(), elementIndex: new Map() } })
    await vi.waitFor(() => {
      expect(useFloatingPanelStore.getState().panels[0].contextStatus).toBe('stale')
    })
  })

  it('leaves panels with no context association untouched by layer events', () => {
    viewerEngine.addLayer({ id: 'ctx-layer-3', kind: 'model' })
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'ctx-panel-3',
      typeKey: TEST_TYPE_KEY,
      title: 'No Ctx',
      data: { label: 'x' },
    })

    viewerEngine.removeLayer('ctx-layer-3')

    expect(useFloatingPanelStore.getState().panels[0].contextStatus).toBeNull()
  })
})

describe('floatingPanelStore.markLivePanelKindUnavailable (specs/050 FR-036)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('marks an open panel of the withdrawn kind as unavailable rather than leaving it rendering', () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'withdraw-1',
      typeKey: TEST_TYPE_KEY,
      title: 'Test',
      data: { label: 'ok' },
    })
    expect(useFloatingPanelStore.getState().panels[0].validationStatus).toBe('valid')

    useFloatingPanelStore.getState().markLivePanelKindUnavailable(TEST_TYPE_KEY)

    expect(useFloatingPanelStore.getState().panels[0].validationStatus).toBe('unknown-type')
  })

  it('leaves panels of a different kind untouched', () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'withdraw-2',
      typeKey: TEST_TYPE_KEY,
      title: 'Test',
      data: { label: 'ok' },
    })

    useFloatingPanelStore.getState().markLivePanelKindUnavailable('some-other-kind')

    expect(useFloatingPanelStore.getState().panels[0].validationStatus).toBe('valid')
  })

  it('leaves content panels untouched', () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'content',
      requestId: 'withdraw-3',
      title: 'Test',
      content: { version: 1, blocks: [{ kind: 'text', text: 'hello' }] },
    })

    useFloatingPanelStore.getState().markLivePanelKindUnavailable(TEST_TYPE_KEY)

    expect(useFloatingPanelStore.getState().panels[0].validationStatus).toBe('valid')
  })
})

/** Re-opening an already-open panel id is a REFRESH, not a re-creation. Found in review of
 * specs/052: the solar figures panel refreshes its content on every playback tick via a stable
 * `requestId`, and the previous behaviour rebuilt the panel from scratch each time — resetting
 * position and size, un-minimizing it, and stealing z-order at frame rate. */
describe('floatingPanelStore.openPanel — refreshing an already-open panel', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  function openFigures(text: string) {
    useFloatingPanelStore.getState().openPanel({
      kind: 'content',
      requestId: 'refresh-me',
      title: 'Figures',
      content: { version: 1, blocks: [{ kind: 'text', text }] },
    })
  }

  it('updates the content but preserves position, size, minimized state and z-order', () => {
    openFigures('first')
    const id = useFloatingPanelStore.getState().panels[0].id

    useFloatingPanelStore.getState().updatePosition(id, { x: 640, y: 480 })
    useFloatingPanelStore.getState().updateSize(id, { width: 500, height: 300 })
    useFloatingPanelStore.getState().minimizePanel(id)
    const before = useFloatingPanelStore.getState().panels.find((p) => p.id === id)!

    openFigures('second')

    const after = useFloatingPanelStore.getState().panels.find((p) => p.id === id)!
    expect(useFloatingPanelStore.getState().panels).toHaveLength(1)
    // Content refreshed...
    expect(after.content).toEqual({ version: 1, blocks: [{ kind: 'text', text: 'second' }] })
    // ...everything the user controls preserved.
    expect(after.position).toEqual({ x: 640, y: 480 })
    expect(after.size).toEqual({ width: 500, height: 300 })
    expect(after.minimized).toBe(true)
    expect(after.restoreState).toEqual(before.restoreState)
    expect(after.zOrder).toBe(before.zOrder)
  })

  it('does not steal focus from another panel when refreshed', () => {
    openFigures('first')
    useFloatingPanelStore.getState().openPanel({
      kind: 'content',
      requestId: 'other',
      title: 'Other',
      content: { version: 1, blocks: [{ kind: 'text', text: 'other' }] },
    })
    const otherZOrder = useFloatingPanelStore.getState().panels.find((p) => p.id === 'other')!.zOrder

    openFigures('second')

    const figures = useFloatingPanelStore.getState().panels.find((p) => p.id === 'refresh-me')!
    expect(figures.zOrder).toBeLessThan(otherZOrder)
  })

  it('consumes no additional cascade slot when refreshed', () => {
    openFigures('first')
    const cascadeAfterOpen = useFloatingPanelStore.getState().cascadeIndex

    openFigures('second')

    expect(useFloatingPanelStore.getState().cascadeIndex).toBe(cascadeAfterOpen)
  })

  it('still places a genuinely new panel normally', () => {
    openFigures('first')
    useFloatingPanelStore.getState().openPanel({
      kind: 'content',
      requestId: 'brand-new',
      title: 'New',
      content: { version: 1, blocks: [{ kind: 'text', text: 'new' }] },
    })

    expect(useFloatingPanelStore.getState().panels).toHaveLength(2)
    expect(useFloatingPanelStore.getState().cascadeIndex).toBe(2)
  })
})

/** specs/054 FR-005d/D5 — `manuallyPlaced` records that a panel's current position/size came from
 * the user, not from automatic placement, so the arrangement engine can treat it as an obstacle
 * rather than a target. */
describe('floatingPanelStore.manuallyPlaced (specs/054 FR-005d)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('starts false for a freshly opened panel', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'mp-1', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    expect(useFloatingPanelStore.getState().panels[0].manuallyPlaced).toBe(false)
  })

  it('updatePosition sets it true', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'mp-2', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    useFloatingPanelStore.getState().updatePosition('mp-2', { x: 10, y: 10 })
    expect(useFloatingPanelStore.getState().panels[0].manuallyPlaced).toBe(true)
  })

  it('updateSize sets it true', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'mp-3', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    useFloatingPanelStore.getState().updateSize('mp-3', { width: 500, height: 400 })
    expect(useFloatingPanelStore.getState().panels[0].manuallyPlaced).toBe(true)
  })

  it('clampToViewport does NOT set it — a resize-driven nudge is not the user choosing a spot', () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'mp-4',
      typeKey: TEST_TYPE_KEY,
      title: 'A',
      data: { label: 'a' },
      position: { x: 900, y: 700 },
    })
    useFloatingPanelStore.getState().clampToViewport({ width: 800, height: 600 })
    expect(useFloatingPanelStore.getState().panels[0].manuallyPlaced).toBe(false)
  })

  it('arrangeAll clears the flag for every panel, including ones the user moved', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'mp-5', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    useFloatingPanelStore.getState().updatePosition('mp-5', { x: 10, y: 10 })
    expect(useFloatingPanelStore.getState().panels[0].manuallyPlaced).toBe(true)

    useFloatingPanelStore.getState().arrangeAll()

    expect(useFloatingPanelStore.getState().panels[0].manuallyPlaced).toBe(false)
  })

  it('a content-kind panel and a live-kind panel are marked manuallyPlaced identically (FR-012)', () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'content',
      requestId: 'mp-content',
      title: 'Content',
      content: { version: 1, blocks: [{ kind: 'text', text: 'hi' }] },
    })
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'mp-live', typeKey: TEST_TYPE_KEY, title: 'Live', data: { label: 'a' } })

    useFloatingPanelStore.getState().updatePosition('mp-content', { x: 5, y: 5 })
    useFloatingPanelStore.getState().updatePosition('mp-live', { x: 5, y: 5 })

    const panels = useFloatingPanelStore.getState().panels
    expect(panels.find((p) => p.id === 'mp-content')!.manuallyPlaced).toBe(true)
    expect(panels.find((p) => p.id === 'mp-live')!.manuallyPlaced).toBe(true)
  })
})

describe('floatingPanelStore.applyArrangement (specs/054)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('writes positions for every panel present in the map', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'aa-1', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'aa-2', typeKey: TEST_TYPE_KEY, title: 'B', data: { label: 'b' } })

    const positions = new Map([
      ['aa-1', { x: 11, y: 22 }],
      ['aa-2', { x: 33, y: 44 }],
    ])
    useFloatingPanelStore.getState().applyArrangement(positions, null)

    const panels = useFloatingPanelStore.getState().panels
    expect(panels.find((p) => p.id === 'aa-1')!.position).toEqual({ x: 11, y: 22 })
    expect(panels.find((p) => p.id === 'aa-2')!.position).toEqual({ x: 33, y: 44 })
  })

  it('leaves manuallyPlaced untouched — applying an arrangement is not a user gesture', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'aa-3', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    useFloatingPanelStore.getState().applyArrangement(new Map([['aa-3', { x: 1, y: 1 }]]), null)
    expect(useFloatingPanelStore.getState().panels[0].manuallyPlaced).toBe(false)
  })

  it('leaves a panel not present in the positions map untouched (e.g. a still-pinned one)', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'aa-4', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    const before = useFloatingPanelStore.getState().panels[0].position
    useFloatingPanelStore.getState().applyArrangement(new Map(), null)
    expect(useFloatingPanelStore.getState().panels[0].position).toEqual(before)
  })

  it('translates cascade z-order ranks into strictly increasing zOrder values, preserving relative order', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'aa-5', typeKey: TEST_TYPE_KEY, title: 'Large', data: { label: 'a' } })
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'aa-6', typeKey: TEST_TYPE_KEY, title: 'Small', data: { label: 'b' } })

    const positions = new Map([
      ['aa-5', { x: 0, y: 0 }],
      ['aa-6', { x: 10, y: 10 }],
    ])
    // rank 1 = largest (lowest z), rank 2 = smallest (highest z) — mirrors contracts/arrangement.md A5.
    const zOrder = new Map([
      ['aa-5', 1],
      ['aa-6', 2],
    ])
    useFloatingPanelStore.getState().applyArrangement(positions, zOrder)

    const panels = useFloatingPanelStore.getState().panels
    const large = panels.find((p) => p.id === 'aa-5')!
    const small = panels.find((p) => p.id === 'aa-6')!
    expect(small.zOrder).toBeGreaterThan(large.zOrder)
  })
})

/** specs/054 FR-006/FR-009/FR-010/FR-011/FR-012 — closing moves a panel into a bounded reopen
 * tray instead of discarding it; reopening restores it via the normal openPanel path. */
describe('floatingPanelStore reopen tray (specs/054)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('moves a closed panel into closedPanels as a reconstructed request, removing it from panels', () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'tray-1',
      typeKey: TEST_TYPE_KEY,
      title: 'Tray Title',
      data: { label: 'x' },
    })
    useFloatingPanelStore.getState().closePanel('tray-1')

    const state = useFloatingPanelStore.getState()
    expect(state.panels).toHaveLength(0)
    expect(state.closedPanels).toHaveLength(1)
    expect(state.closedPanels[0].request).toEqual({
      kind: 'live',
      requestId: 'tray-1',
      typeKey: TEST_TYPE_KEY,
      title: 'Tray Title',
      data: { label: 'x' },
      chrome: { titleBar: true, resizable: true, defaultSize: { width: 320, height: 240 } },
      contextAssociation: null,
    })
    expect(state.closedPanels[0].closedAtUtc).toBeGreaterThan(0)
  })

  it('reconstructs a content-kind request identically to a live-kind one (FR-012)', () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'content',
      requestId: 'tray-content',
      title: 'Content Panel',
      content: { version: 1, blocks: [{ kind: 'text', text: 'hi' }] },
    })
    useFloatingPanelStore.getState().closePanel('tray-content')

    const entry = useFloatingPanelStore.getState().closedPanels[0]
    expect(entry.request).toEqual({
      kind: 'content',
      requestId: 'tray-content',
      title: 'Content Panel',
      content: { version: 1, blocks: [{ kind: 'text', text: 'hi' }] },
      chrome: expect.any(Object),
      contextAssociation: null,
    })
  })

  it('caps closedPanels at MAX_CONCURRENT_PANELS, dropping the oldest entry first', () => {
    const store = useFloatingPanelStore.getState()
    for (let i = 0; i < MAX_CONCURRENT_PANELS + 1; i += 1) {
      store.openPanel({ kind: 'live', requestId: `tray-cap-${i}`, typeKey: TEST_TYPE_KEY, title: 'T', data: { label: 'x' } })
      store.closePanel(`tray-cap-${i}`)
    }

    const closedPanels = useFloatingPanelStore.getState().closedPanels
    expect(closedPanels).toHaveLength(MAX_CONCURRENT_PANELS)
    // Most-recent-first: the very first closed (tray-cap-0) is the oldest and should be gone.
    expect(closedPanels.some((e) => e.request.requestId === 'tray-cap-0')).toBe(false)
    expect(closedPanels.some((e) => e.request.requestId === `tray-cap-${MAX_CONCURRENT_PANELS}`)).toBe(true)
  })

  it('closing a panel whose id already has a tray entry replaces it, not duplicates it', () => {
    const store = useFloatingPanelStore.getState()
    store.openPanel({ kind: 'live', requestId: 'tray-reclose', typeKey: TEST_TYPE_KEY, title: 'First', data: { label: 'first' } })
    store.closePanel('tray-reclose')
    expect(useFloatingPanelStore.getState().closedPanels).toHaveLength(1)

    store.reopenPanel('tray-reclose')
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'tray-reclose',
      typeKey: TEST_TYPE_KEY,
      title: 'Second',
      data: { label: 'second' },
    })
    useFloatingPanelStore.getState().closePanel('tray-reclose')

    const closedPanels = useFloatingPanelStore.getState().closedPanels
    expect(closedPanels).toHaveLength(1)
    expect(closedPanels[0].request.title).toBe('Second')
  })

  it('reopenPanel removes the entry and reopens it via openPanel with the original content', () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'tray-reopen',
      typeKey: TEST_TYPE_KEY,
      title: 'Reopen Me',
      data: { label: 'original' },
    })
    useFloatingPanelStore.getState().closePanel('tray-reopen')
    expect(useFloatingPanelStore.getState().panels).toHaveLength(0)

    useFloatingPanelStore.getState().reopenPanel('tray-reopen')

    const state = useFloatingPanelStore.getState()
    expect(state.closedPanels).toHaveLength(0)
    expect(state.panels).toHaveLength(1)
    expect(state.panels[0].title).toBe('Reopen Me')
    expect(state.panels[0].data).toEqual({ label: 'original' })
    expect(state.panels[0].validationStatus).toBe('valid')
  })

  it('reopen behaves identically for a content-kind and a live-kind panel (FR-012)', () => {
    const store = useFloatingPanelStore.getState()
    store.openPanel({
      kind: 'content',
      requestId: 'tray-reopen-content',
      title: 'Content',
      content: { version: 1, blocks: [{ kind: 'text', text: 'hi' }] },
    })
    store.openPanel({ kind: 'live', requestId: 'tray-reopen-live', typeKey: TEST_TYPE_KEY, title: 'Live', data: { label: 'x' } })
    store.closePanel('tray-reopen-content')
    store.closePanel('tray-reopen-live')
    expect(useFloatingPanelStore.getState().panels).toHaveLength(0)

    useFloatingPanelStore.getState().reopenPanel('tray-reopen-content')
    useFloatingPanelStore.getState().reopenPanel('tray-reopen-live')

    const panels = useFloatingPanelStore.getState().panels
    expect(panels.find((p) => p.id === 'tray-reopen-content')?.validationStatus).toBe('valid')
    expect(panels.find((p) => p.id === 'tray-reopen-live')?.validationStatus).toBe('valid')
    expect(useFloatingPanelStore.getState().closedPanels).toHaveLength(0)
  })

  it('is a no-op when reopening an id with no tray entry', () => {
    expect(() => useFloatingPanelStore.getState().reopenPanel('does-not-exist')).not.toThrow()
    expect(useFloatingPanelStore.getState().panels).toHaveLength(0)
  })

  it("FR-013: a panel reopened after its associated layer was removed while it sat in the tray shows 'invalid', not 'current'", () => {
    viewerEngine.addLayer({ id: 'tray-ctx-layer', kind: 'model' })
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'tray-ctx-panel',
      typeKey: TEST_TYPE_KEY,
      title: 'Ctx',
      data: { label: 'x' },
      contextAssociation: { layerId: 'tray-ctx-layer' },
    })
    expect(useFloatingPanelStore.getState().panels[0].contextStatus).toBe('current')

    useFloatingPanelStore.getState().closePanel('tray-ctx-panel')
    viewerEngine.removeLayer('tray-ctx-layer')

    useFloatingPanelStore.getState().reopenPanel('tray-ctx-panel')

    const reopened = useFloatingPanelStore.getState().panels.find((p) => p.id === 'tray-ctx-panel')!
    expect(reopened.contextStatus).toBe('invalid')
  })

  it('minimizePanel never adds a closedPanels entry', () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'tray-minimize',
      typeKey: TEST_TYPE_KEY,
      title: 'Minimized',
      data: { label: 'x' },
    })
    useFloatingPanelStore.getState().minimizePanel('tray-minimize')

    expect(useFloatingPanelStore.getState().closedPanels).toHaveLength(0)
    expect(useFloatingPanelStore.getState().panels[0].minimized).toBe(true)
  })
})

describe('floatingPanelStore.withdrawPanel', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  function openWithdrawable(requestId: string) {
    useFloatingPanelStore.getState().openPanel({
      kind: 'content',
      requestId,
      title: requestId,
      content: { version: 1, blocks: [{ kind: 'text', text: requestId }] },
    })
  }

  it('removes the panel without offering it in the reopen tray', () => {
    openWithdrawable('withdraw-me')
    openWithdrawable('keep-me')

    useFloatingPanelStore.getState().withdrawPanel('withdraw-me')

    expect(useFloatingPanelStore.getState().panels.map((p) => p.id)).toEqual(['keep-me'])
    expect(useFloatingPanelStore.getState().closedPanels).toHaveLength(0)
  })

  it('also drops a tray entry left by an earlier user close of the same panel', () => {
    openWithdrawable('closed-earlier')
    useFloatingPanelStore.getState().closePanel('closed-earlier')
    expect(useFloatingPanelStore.getState().closedPanels).toHaveLength(1)

    useFloatingPanelStore.getState().withdrawPanel('closed-earlier')

    expect(useFloatingPanelStore.getState().closedPanels).toHaveLength(0)
  })
})
