import { beforeEach, describe, expect, it } from 'vitest'
import { OUTLINE_TOOL_GROUPS, rememberedTool, useOutlineToolGroupStore } from './outlineToolGroupStore'

beforeEach(() => {
  localStorage.clear()
  useOutlineToolGroupStore.setState({ chosen: {} })
})

// specs/081 US6 (T043): each group shows the tool last chosen from it, persisted per device.
describe('outlineToolGroupStore', () => {
  it('shows each group\'s default tool until a tool is chosen from it', () => {
    expect(rememberedTool('circle', {})).toBe(OUTLINE_TOOL_GROUPS.circle.defaultTool)
    expect(rememberedTool('circle', { circle: 'cut-circle' })).toBe('cut-circle')
  })

  it('remembers the chosen tool per group and persists it to localStorage', () => {
    useOutlineToolGroupStore.getState().choose('circle', 'cut-circle')
    expect(useOutlineToolGroupStore.getState().chosen.circle).toBe('cut-circle')
    expect(JSON.parse(localStorage.getItem('outline-tool-groups')!).state.chosen).toEqual({ circle: 'cut-circle' })
  })

  it('has six groups whose defaults belong to them', () => {
    expect(Object.keys(OUTLINE_TOOL_GROUPS)).toHaveLength(6)
    for (const group of Object.values(OUTLINE_TOOL_GROUPS)) expect(group.tools).toContain(group.defaultTool)
  })
})
