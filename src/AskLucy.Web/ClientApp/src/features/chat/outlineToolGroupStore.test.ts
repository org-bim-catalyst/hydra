import { beforeEach, describe, expect, it } from 'vitest'
import { OUTLINE_TOOL_GROUPS, orderOf, useOutlineToolGroupStore } from './outlineToolGroupStore'

beforeEach(() => {
  localStorage.clear()
  useOutlineToolGroupStore.setState({ order: {} })
})

// specs/081 US6 (T043): choosing a tool from a group's flyout swaps it with the group button's tool, persisted per device.
describe('outlineToolGroupStore', () => {
  it("starts each group in its default order, with the default tool first", () => {
    expect(orderOf('circle', {})).toEqual([...OUTLINE_TOOL_GROUPS.circle.tools])
    expect(orderOf('circle', {})[0]).toBe(OUTLINE_TOOL_GROUPS.circle.defaultTool)
  })

  it("swaps the chosen tool with the button's tool, so the two trade places", () => {
    useOutlineToolGroupStore.getState().choose('circle', 'cut-circle')
    expect(orderOf('circle', useOutlineToolGroupStore.getState().order)).toEqual(['cut-circle', 'add-circle', 'make-circle'])
  })

  it('swaps back when the first tool is chosen again from the flyout', () => {
    const { choose } = useOutlineToolGroupStore.getState()
    choose('circle', 'cut-circle')
    choose('circle', 'add-circle')
    expect(orderOf('circle', useOutlineToolGroupStore.getState().order)).toEqual(['add-circle', 'cut-circle', 'make-circle'])
  })

  it('persists the order to localStorage', () => {
    useOutlineToolGroupStore.getState().choose('circle', 'make-circle')
    expect(JSON.parse(localStorage.getItem('outline-tool-groups')!).state.order.circle).toEqual(['make-circle', 'cut-circle', 'add-circle'])
  })

  it('has six groups whose defaults belong to them', () => {
    expect(Object.keys(OUTLINE_TOOL_GROUPS)).toHaveLength(6)
    for (const group of Object.values(OUTLINE_TOOL_GROUPS)) expect(group.tools).toContain(group.defaultTool)
  })
})

describe('orderOf with a saved order from before a tool was added', () => {
  it('keeps the saved order and appends the tools it does not list', () => {
    expect(orderOf('rectangle', { rectangle: ['cut-rectangle', 'add-rectangle', 'cut-square', 'add-square'] })).toEqual([
      'cut-rectangle',
      'add-rectangle',
      'cut-square',
      'add-square',
      'typed-rectangle',
      'typed-square',
    ])
  })
})
