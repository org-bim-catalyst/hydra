import { beforeEach, describe, expect, it } from 'vitest'
import { z } from 'zod'
import { panelTypeRegistry } from '../registry'
import { MAX_CONCURRENT_PANELS } from '../types/panel'
import { useFloatingPanelStore } from './floatingPanelStore'

const TEST_TYPE_KEY = `test-panel-${Math.random()}`

panelTypeRegistry.register({
  typeKey: TEST_TYPE_KEY,
  renderer: () => null,
  schema: z.object({ label: z.string() }),
  defaultSize: { width: 320, height: 240 },
  resizable: true,
})

const initialState = useFloatingPanelStore.getState()

describe('floatingPanelStore.openPanel validation states', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('marks a panel valid when the type is registered and data matches its schema', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ requestId: 'p1', typeKey: TEST_TYPE_KEY, title: 'Test', data: { label: 'ok' } })
    const panel = useFloatingPanelStore.getState().panels[0]
    expect(panel.validationStatus).toBe('valid')
    expect(panel.data).toEqual({ label: 'ok' })
  })

  it('marks a panel unknown-type when the typeKey has no registered definition', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ requestId: 'p2', typeKey: 'does-not-exist', title: 'Test', data: {} })
    const panel = useFloatingPanelStore.getState().panels[0]
    expect(panel.validationStatus).toBe('unknown-type')
  })

  it('marks a panel invalid when data fails the resolved schema, with a validationError set', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ requestId: 'p3', typeKey: TEST_TYPE_KEY, title: 'Test', data: { nonsense: true } })
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
    store.openPanel({ requestId: 'c1', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    store.openPanel({ requestId: 'c2', typeKey: TEST_TYPE_KEY, title: 'B', data: { label: 'b' } })
    const [first, second] = useFloatingPanelStore.getState().panels
    expect(second.position.x).toBeGreaterThan(first.position.x)
    expect(second.position.y).toBeGreaterThan(first.position.y)
  })

  it('wraps back toward the starting corner after enough panels', () => {
    const store = useFloatingPanelStore.getState()
    for (let i = 0; i < 10; i += 1) {
      store.openPanel({ requestId: `wrap-${i}`, typeKey: TEST_TYPE_KEY, title: 'W', data: { label: 'w' } })
    }
    // Focus wrap-0 so it isn't the least-recently-focused panel once the cap-triggering 11th
    // panel is opened below (MAX_CONCURRENT_PANELS is also 10) — this test is about the cascade
    // offset math wrapping, not eviction (covered separately), so keep wrap-0 alive to compare.
    store.focusPanel('wrap-0')
    store.openPanel({ requestId: 'wrap-10', typeKey: TEST_TYPE_KEY, title: 'W', data: { label: 'w' } })

    const panels = useFloatingPanelStore.getState().panels
    const first = panels.find((p) => p.id === 'wrap-0')!
    const eleventh = panels.find((p) => p.id === 'wrap-10')!
    expect(eleventh.position).toEqual(first.position)
  })

  it('does not consume a cascade slot when a position is explicitly supplied', () => {
    const store = useFloatingPanelStore.getState()
    store.openPanel({
      requestId: 'explicit',
      typeKey: TEST_TYPE_KEY,
      title: 'Explicit',
      data: { label: 'e' },
      position: { x: 999, y: 999 },
    })
    store.openPanel({ requestId: 'cascaded', typeKey: TEST_TYPE_KEY, title: 'Cascaded', data: { label: 'c' } })
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
      store.openPanel({ requestId: `cap-${i}`, typeKey: TEST_TYPE_KEY, title: 'Cap', data: { label: 'cap' } })
    }
    expect(useFloatingPanelStore.getState().panels).toHaveLength(MAX_CONCURRENT_PANELS)

    store.openPanel({ requestId: 'cap-overflow', typeKey: TEST_TYPE_KEY, title: 'Overflow', data: { label: 'o' } })

    const panels = useFloatingPanelStore.getState().panels
    expect(panels).toHaveLength(MAX_CONCURRENT_PANELS)
    expect(panels.some((p) => p.id === 'cap-0')).toBe(false)
    expect(panels.some((p) => p.id === 'cap-overflow')).toBe(true)
  })

  it('never blocks the request that would exceed the cap — it always succeeds', () => {
    const store = useFloatingPanelStore.getState()
    for (let i = 0; i < MAX_CONCURRENT_PANELS + 1; i += 1) {
      store.openPanel({ requestId: `never-blocked-${i}`, typeKey: TEST_TYPE_KEY, title: 'X', data: { label: 'x' } })
    }
    expect(useFloatingPanelStore.getState().panels.some((p) => p.id === `never-blocked-${MAX_CONCURRENT_PANELS}`)).toBe(
      true,
    )
  })
})
