/** research D4 — the only sanctioned redraw request path (FR-020). Coalesces every `invalidate()`
 * call arriving before the next frame into exactly one downstream redraw (FR-021), and does
 * nothing when nothing has changed (FR-022) — the property `GoogleMapsGisLayer.onDraw`'s previous
 * unconditional `overlay.requestRedraw()` call violated. */
export class RedrawScheduler {
  private pending = false
  private requestRedraw: (() => void) | null = null

  /** Called once by the map bridge to supply the real underlying redraw call
   * (`overlay.requestRedraw`). Never called by a capability. */
  bind(requestRedraw: () => void): void {
    this.requestRedraw = requestRedraw
  }

  /** The one path any capability (or the viewer itself) requests a redraw through. Safe to call
   * with nothing bound yet, and safe to call from a capability that has since stopped (FR-024) —
   * a stopped capability holds no live handle to call it from in the first place; this method
   * itself does not track callers, so there is nothing to reject. */
  invalidate(): void {
    if (this.pending || !this.requestRedraw) return
    this.pending = true
    this.requestRedraw()
  }

  /** Called by the map bridge once its actual draw has run, clearing the coalescing window so the
   * next `invalidate()` call schedules a fresh redraw rather than being swallowed forever. */
  frameRendered(): void {
    this.pending = false
  }
}

/** Single module-level instance, mirroring every other viewer singleton's convention. */
export const redrawScheduler = new RedrawScheduler()
