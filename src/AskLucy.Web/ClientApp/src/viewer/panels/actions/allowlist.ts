import { z } from 'zod'
import type { IViewerEngine } from '../../api/engine'
import type { ViewerCommandResult } from '../../api/commands'

/** contracts/action-allowlist.md — the closed set of viewer operations panel content may invoke.
 * Model output is untrusted (constitution §8): every entry here is an explicit, written-out
 * invoker over the viewer's already-published command surface, never a dynamic dispatch by a
 * content-supplied string (research D6). Adding a command means adding an entry here and to the
 * contract — nothing about a block's own schema needs to change, since `Action` (blocks.ts) is
 * already just `{command, args}` for any command.
 *
 * Deliberately excluded: `addLayer`, `removeLayer`, `displayContent`, `createOverlay`. These
 * mutate what the viewer contains rather than how it is shown, and content composed by a model
 * must not be able to add or destroy viewer content as a side effect of a click
 * (contracts/action-allowlist.md "Deliberately excluded"). specs/051 revisits this list once it
 * defines content commands with the right lifecycle guarantees. `fitBounds` is left out too, for
 * a narrower reason: it exists on the concrete `ViewerEngine` (specs/038) but was never published
 * on `IViewerEngine`, and widening that published interface is exactly the kind of viewer-command
 * change this feature keeps out of scope (spec 049 Constraints) — `zoomToLocation` already covers
 * framing for v1; `fitBounds` can join once 051 settles the published surface it belongs to.
 *
 * specs/051 adds `selectAndFrame` (contracts/action-allowlist-extension.md) — safe because it only
 * selects and re-frames already-loaded content, the same risk profile as `select`. `loadContent`/
 * `replaceContent`/`unloadContent` are deliberately still excluded, for the same reason as
 * `addLayer`/`removeLayer` above: they create or destroy viewer content. */

type Invoker = (engine: IViewerEngine, args: unknown) => ViewerCommandResult

const selectArgsSchema = z.object({ layerId: z.string().min(1), elementId: z.string().min(1) })
const selectAndFrameArgsSchema = z.object({ layerId: z.string().min(1), elementId: z.string().min(1) })
const clearSelectionArgsSchema = z.object({})
const zoomToLocationArgsSchema = z.object({
  latitude: z.number().min(-90).max(90),
  longitude: z.number().min(-180).max(180),
  zoom: z.number().optional(),
})
const setLayerVisibilityArgsSchema = z.object({ layerId: z.string().min(1), visible: z.boolean() })
const setViewModeArgsSchema = z.object({ mode: z.enum(['isometric', 'plan']) })
const setMapStyleArgsSchema = z.object({ mapStyle: z.enum(['roadmap', 'satellite', 'hybrid', 'buildings-only']) })

interface AllowlistEntry {
  argsSchema: z.ZodType
  invoke: Invoker
}

export const actionAllowlist: Record<string, AllowlistEntry> = {
  select: {
    argsSchema: selectArgsSchema,
    invoke: (engine, args) => {
      const { layerId, elementId } = args as z.infer<typeof selectArgsSchema>
      return engine.select(layerId, elementId)
    },
  },
  clearSelection: {
    argsSchema: clearSelectionArgsSchema,
    invoke: (engine) => engine.clearSelection(),
  },
  selectAndFrame: {
    argsSchema: selectAndFrameArgsSchema,
    invoke: (engine, args) => {
      const { layerId, elementId } = args as z.infer<typeof selectAndFrameArgsSchema>
      return engine.selectAndFrame(layerId, elementId)
    },
  },
  zoomToLocation: {
    argsSchema: zoomToLocationArgsSchema,
    invoke: (engine, args) => {
      const { latitude, longitude, zoom } = args as z.infer<typeof zoomToLocationArgsSchema>
      return engine.zoomToLocation(latitude, longitude, zoom)
    },
  },
  setLayerVisibility: {
    argsSchema: setLayerVisibilityArgsSchema,
    invoke: (engine, args) => {
      const { layerId, visible } = args as z.infer<typeof setLayerVisibilityArgsSchema>
      return engine.setLayerVisibility(layerId, visible)
    },
  },
  setViewMode: {
    argsSchema: setViewModeArgsSchema,
    invoke: (engine, args) => {
      const { mode } = args as z.infer<typeof setViewModeArgsSchema>
      return engine.setViewMode(mode)
    },
  },
  setMapStyle: {
    argsSchema: setMapStyleArgsSchema,
    invoke: (engine, args) => {
      const { mapStyle } = args as z.infer<typeof setMapStyleArgsSchema>
      return engine.setMapStyle(mapStyle)
    },
  },
}

export type ActionValidation =
  | { valid: true }
  | { valid: false; reason: string }

/** Validates a content-supplied action against the closed allowlist. Called at render, not on
 * activation — an action that fails here must never be presented as activatable in the first
 * place (spec FR-013/FR-014, research D6), which is stronger than merely refusing on click. Every
 * rejection is logged for diagnosis (spec FR-014) rather than silently discarded — a model
 * consistently attempting a disallowed command should be visible to the team, not invisible. */
export function validateAction(command: string, args: unknown): ActionValidation {
  const entry = actionAllowlist[command]
  if (!entry) {
    console.warn(`[panel action] rejected: "${command}" is not an allowlisted command.`)
    return { valid: false, reason: `"${command}" is not a permitted action.` }
  }

  const parsed = entry.argsSchema.safeParse(args)
  if (!parsed.success) {
    const reason = parsed.error.issues.map((issue) => issue.message).join('; ')
    console.warn(`[panel action] rejected: "${command}" arguments were invalid — ${reason}`)
    return { valid: false, reason: `"${command}" could not be carried out: ${reason}` }
  }

  return { valid: true }
}
