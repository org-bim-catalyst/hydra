import { MIN_PANEL_HEIGHT, MIN_PANEL_WIDTH } from '../types/panel'

/** How tightly a panel lays out its chrome and content. `compact` is the dense look of the
 * solar-analysis reference page — small dim labels, rows on hairline dividers, monospace values —
 * for panels that must leave the scene visible around them. */
export type PanelDensity = 'comfortable' | 'compact'

/** data-model.md "Panel Chrome" (research D7) — how a panel is framed, declared rather than
 * uniform across every panel. A conventional titled box, a wide control strip and a small
 * circular widget with no title bar all exist under this one shape. */
export interface PanelChrome {
  titleBar: boolean
  resizable: boolean
  defaultSize: { width: number; height: number }
  /** Optional so every existing chrome declaration keeps its meaning; absent means `comfortable`. */
  density?: PanelDensity
  /** Optional per-kind resize floor, above the framework-wide `MIN_PANEL_WIDTH`/`MIN_PANEL_HEIGHT`
   * — for content (e.g. solar's tick-marked time slider) that degrades below a size the generic
   * floor allows. Absent means the panel is only bound by the framework-wide minimum. */
  minSize?: { width: number; height: number }
}

export const DEFAULT_CONTENT_CHROME: PanelChrome = {
  titleBar: true,
  resizable: true,
  defaultSize: { width: 400, height: 300 },
}

/** Applies a request's chrome override onto the default, and clamps the resolved default size to
 * the existing minimum-size floor (spec Edge Cases: "resize below a usable minimum size") — a
 * request cannot ask for a panel smaller than the framework already guarantees is usable. */
export function resolveChrome(override: Partial<PanelChrome> | null | undefined, base: PanelChrome = DEFAULT_CONTENT_CHROME): PanelChrome {
  const minSize = override?.minSize ?? base.minSize
  const minWidth = Math.max(minSize?.width ?? MIN_PANEL_WIDTH, MIN_PANEL_WIDTH)
  const minHeight = Math.max(minSize?.height ?? MIN_PANEL_HEIGHT, MIN_PANEL_HEIGHT)
  const merged: PanelChrome = {
    titleBar: override?.titleBar ?? base.titleBar,
    resizable: override?.resizable ?? base.resizable,
    defaultSize: {
      width: Math.max(override?.defaultSize?.width ?? base.defaultSize.width, minWidth),
      height: Math.max(override?.defaultSize?.height ?? base.defaultSize.height, minHeight),
    },
  }
  const density = override?.density ?? base.density
  if (density) merged.density = density
  if (minSize) merged.minSize = minSize
  return merged
}
