import { useSiteBoundaryEditStore, type ShapeTool } from './siteBoundaryEditStore'

/**
 * specs/079: what the outline menu, the toolbar and the keyboard can ask edit mode to do. The
 * implementation needs the live map, which only exists inside the viewer, so it registers itself
 * here (see `useSiteBoundaryEditMode`); callers elsewhere - the workspace's Outline menu sits
 * outside the viewer - go through this module and never touch the map.
 */
export interface SiteBoundaryEditRuntime {
  /** `requestedRevision` is the outline revision Lucy opened the editor for; a different one in the viewer is refetched first. */
  start(requestedRevision?: string): Promise<void>
  done(): Promise<void>
  cancel(): void
  undo(): void
  redo(): void
  addCorner(): void
  /** Deletes the selected corner, or every selected corner at once. */
  deleteCorner(): void
  /** Switches between moving corners (Google's handles) and drawing a box that selects them. */
  toggleSelectTool(): void
  /** Opens the dialog for a shape tool, unless the tool cannot act yet (no corner selected). */
  openShapeDialog(tool: ShapeTool): void
  /** Applies the shape tool with the number from its dialog. True on success; false with the reason shown. */
  applyShape(tool: ShapeTool, value: number): boolean
  loadLatest(): Promise<void>
}

let runtime: SiteBoundaryEditRuntime | null = null

/** Returns a function that unregisters this runtime, but only if it is still the registered one. */
export function registerSiteBoundaryEditRuntime(next: SiteBoundaryEditRuntime): () => void {
  runtime = next
  return () => {
    if (runtime === next) runtime = null
  }
}

const NOT_READY = "The outline editor isn't ready yet — the map is still loading."

/** The action, or - when nothing has registered (the viewer is not showing a map) - a visible explanation, never a silent no-op. */
function call<K extends keyof SiteBoundaryEditRuntime>(
  name: K,
  ...args: Parameters<SiteBoundaryEditRuntime[K]>
): ReturnType<SiteBoundaryEditRuntime[K]> | undefined {
  if (!runtime) {
    useSiteBoundaryEditStore.getState().setNotice(NOT_READY)
    return undefined
  }
  return (runtime[name] as (...a: Parameters<SiteBoundaryEditRuntime[K]>) => ReturnType<SiteBoundaryEditRuntime[K]>)(...args)
}

export const siteBoundaryEditActions = {
  start: (requestedRevision?: string) => call('start', requestedRevision),
  done: () => call('done'),
  cancel: () => call('cancel'),
  undo: () => call('undo'),
  redo: () => call('redo'),
  addCorner: () => call('addCorner'),
  deleteCorner: () => call('deleteCorner'),
  toggleSelectTool: () => call('toggleSelectTool'),
  openShapeDialog: (tool: ShapeTool) => call('openShapeDialog', tool),
  applyShape: (tool: ShapeTool, value: number) => call('applyShape', tool, value) ?? false,
  loadLatest: () => call('loadLatest'),
}
