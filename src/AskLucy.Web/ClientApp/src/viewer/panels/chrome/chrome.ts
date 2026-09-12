import { MIN_PANEL_HEIGHT, MIN_PANEL_WIDTH } from '../types/panel'

/** data-model.md "Panel Chrome" (research D7) — how a panel is framed, declared rather than
 * uniform across every panel. A conventional titled box, a wide control strip and a small
 * circular widget with no title bar all exist under this one shape. */
export interface PanelChrome {
  titleBar: boolean
  resizable: boolean
  defaultSize: { width: number; height: number }
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
  const merged: PanelChrome = {
    titleBar: override?.titleBar ?? base.titleBar,
    resizable: override?.resizable ?? base.resizable,
    defaultSize: {
      width: Math.max(override?.defaultSize?.width ?? base.defaultSize.width, MIN_PANEL_WIDTH),
      height: Math.max(override?.defaultSize?.height ?? base.defaultSize.height, MIN_PANEL_HEIGHT),
    },
  }
  return merged
}
