import { create } from 'zustand'
import { persist } from 'zustand/middleware'

/**
 * specs/081 US6 (T043): the outline editor's ribbon groups their tools, and each group remembers the tool
 * last chosen from it, so the group button keeps showing that tool's icon. Persisted per device.
 */
export const OUTLINE_TOOL_GROUPS = {
  'edit-corners': { defaultTool: 'select-corners', tools: ['select-corners', 'delete-corner'] },
  'add-corner': { defaultTool: 'add-corner', tools: ['add-corner', 'round-corner'] },
  'curve-edge': { defaultTool: 'curve-edge', tools: ['curve-edge', 'draw-arc'] },
  circle: { defaultTool: 'add-circle', tools: ['add-circle', 'cut-circle', 'make-circle'] },
  rectangle: { defaultTool: 'add-rectangle', tools: ['add-rectangle', 'cut-rectangle', 'add-square', 'cut-square'] },
  polygon: { defaultTool: 'add-polygon', tools: ['add-polygon', 'cut-polygon'] },
} as const

export type OutlineToolGroupId = keyof typeof OUTLINE_TOOL_GROUPS

interface OutlineToolGroupState {
  /** The last tool chosen from each group, by group id. Missing means the group's default. */
  chosen: Partial<Record<OutlineToolGroupId, string>>
  choose: (group: OutlineToolGroupId, tool: string) => void
}

export const useOutlineToolGroupStore = create<OutlineToolGroupState>()(
  persist(
    (set, get) => ({
      chosen: {},
      choose: (group, tool) => set({ chosen: { ...get().chosen, [group]: tool } }),
    }),
    { name: 'outline-tool-groups', partialize: (state) => ({ chosen: state.chosen }) },
  ),
)

/** The tool a group shows when nothing in it is active: the one last chosen, else its default. */
export function rememberedTool(group: OutlineToolGroupId, chosen: Partial<Record<OutlineToolGroupId, string>>): string {
  return chosen[group] ?? OUTLINE_TOOL_GROUPS[group].defaultTool
}
