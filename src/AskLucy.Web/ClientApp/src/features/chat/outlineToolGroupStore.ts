import { create } from 'zustand'
import { persist } from 'zustand/middleware'

/**
 * specs/081 US6 (T043): the outline editor's ribbon groups its tools. Each group keeps its tools in display
 * order; the first is the one its button shows. Choosing a tool from a group's flyout swaps it with the button's
 * tool, so the two trade places. Persisted per device.
 */
export const OUTLINE_TOOL_GROUPS = {
  'edit-corners': { defaultTool: 'select-corners', tools: ['select-corners', 'delete-corner'] },
  'add-corner': { defaultTool: 'add-corner', tools: ['add-corner', 'round-corner'] },
  'curve-edge': { defaultTool: 'curve-edge', tools: ['curve-edge', 'draw-arc'] },
  circle: { defaultTool: 'add-circle', tools: ['add-circle', 'cut-circle', 'make-circle'] },
  rectangle: {
    defaultTool: 'add-rectangle',
    tools: ['add-rectangle', 'cut-rectangle', 'add-square', 'cut-square', 'typed-shape'],
  },
  polygon: { defaultTool: 'add-polygon', tools: ['add-polygon', 'cut-polygon'] },
} as const

export type OutlineToolGroupId = keyof typeof OUTLINE_TOOL_GROUPS

interface OutlineToolGroupState {
  /** Each group's tools in display order, by group id. Missing means the group's defaults. */
  order: Partial<Record<OutlineToolGroupId, string[]>>
  /** Swaps `tool` with the group button's tool. Choosing the button's own tool changes nothing. */
  choose: (group: OutlineToolGroupId, tool: string) => void
}

export const useOutlineToolGroupStore = create<OutlineToolGroupState>()(
  persist(
    (set, get) => ({
      order: {},
      choose: (group, tool) => {
        const current = orderOf(group, get().order)
        const index = current.indexOf(tool)
        if (index <= 0) return
        const next = [...current]
        ;[next[0], next[index]] = [next[index], next[0]]
        set({ order: { ...get().order, [group]: next } })
      },
    }),
    { name: 'outline-tool-groups', partialize: (state) => ({ order: state.order }) },
  ),
)

/** A group's tools in display order: the first is the button's. */
export function orderOf(group: OutlineToolGroupId, order: Partial<Record<OutlineToolGroupId, string[]>>): string[] {
  const defaults: readonly string[] = OUTLINE_TOOL_GROUPS[group].tools
  const saved = (order[group] ?? []).filter((tool) => defaults.includes(tool))
  // A saved order predates any tool added since; those join the end, so a new tool is never hidden by an old order.
  return [...saved, ...defaults.filter((tool) => !saved.includes(tool))]
}
