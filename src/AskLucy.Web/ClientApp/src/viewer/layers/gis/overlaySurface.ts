import * as THREE from 'three'

/** A transparent drawing surface stacked above the map's own canvas (specs/087).
 *
 * The map renders its 3D buildings *after* `WebGLOverlayView`'s draw callback and ignores the
 * depth written there, so nothing drawn inside the map's pass can stay in front of a building.
 * This surface is a second, independent WebGL context on its own canvas, composited above the map
 * — the only arrangement a later pass cannot paint over.
 */
export interface OverlaySurface {
  readonly canvas: HTMLCanvasElement
  readonly renderer: THREE.WebGLRenderer
  /** Sets the drawing buffer to `width × height` device pixels; a no-op when unchanged or disposed. */
  resize(width: number, height: number): void
  /** Puts the canvas's CSS box exactly on the map's own canvas (specs/087 D4, corrected live
   * 2026-10-26: Maps sized its canvas to 1592×868 inside a 1600×872 container at dpr 2, so the
   * `inset: 0` box stretched the dome ~0.5% and left it drifting against the map as the camera
   * moved). Called wherever the drawing buffer is resized — the same moments the map canvas's box
   * can change — and a no-op when that box is not laid out yet. Accepts the union Maps types its
   * context's canvas with; an `OffscreenCanvas` has no box, and is skipped. */
  alignTo(mapCanvas: HTMLCanvasElement | OffscreenCanvas): void
  /** Releases the context, every GPU resource and the canvas. Safe to call twice. */
  dispose(): void
}

/** `pointer-events: none` keeps every map gesture with the map. The four offsets are only the
 * starting box, for the instant before the first `alignTo()`: Maps does not always size its canvas
 * to the container's box, so the real box is read from the map's own canvas (see `alignTo`). The
 * z-index puts the canvas above Maps' own canvas, which it renders at z-index 0. */
const CANVAS_STYLE = 'position:absolute;top:0;right:0;bottom:0;left:0;pointer-events:none;z-index:1;'

/** Writes a CSS length only when it actually changes, so a per-draw alignment never churns style. */
function setStyleLength(element: HTMLCanvasElement, property: 'left' | 'top' | 'width' | 'height', value: number): void {
  const next = `${value}px`
  if (element.style[property] !== next) element.style[property] = next
}

/** Throws when the renderer cannot be created (no WebGL, context limit) — the caller logs it. The
 * optional factory exists so the surface's lifecycle is testable in jsdom, which has no WebGL. */
export function createOverlaySurface(
  container: HTMLElement,
  createRenderer: (canvas: HTMLCanvasElement) => THREE.WebGLRenderer = (canvas) =>
    new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true }),
): OverlaySurface {
  const canvas = document.createElement('canvas')
  canvas.style.cssText = CANVAS_STYLE
  container.appendChild(canvas)

  let renderer: THREE.WebGLRenderer
  try {
    renderer = createRenderer(canvas)
  } catch (error) {
    canvas.remove()
    throw error
  }

  // Matches the interleaved renderer: pixel ratio 1 is a deliberate, measured choice for this
  // page's GPU budget (see GoogleMapsGisLayer's onContextRestored). The colour treatment is
  // mirrored too, so the dome's lit dial and shell look the same on both surfaces.
  renderer.setPixelRatio(1)
  // The bridge renders in two passes (floor layer, then the rest) with an explicit depth clear
  // between them, so it clears the surface itself rather than letting each render() do it.
  renderer.autoClear = false
  renderer.setClearColor(0x000000, 0)
  renderer.outputColorSpace = THREE.SRGBColorSpace
  renderer.toneMapping = THREE.ACESFilmicToneMapping

  let width = -1
  let height = -1
  let disposed = false

  return {
    canvas,
    renderer,
    resize(nextWidth, nextHeight) {
      if (disposed || (nextWidth === width && nextHeight === height)) return
      width = nextWidth
      height = nextHeight
      renderer.setSize(nextWidth, nextHeight, false)
    },
    alignTo(mapCanvas) {
      if (disposed || !(mapCanvas instanceof HTMLCanvasElement)) return
      const host = canvas.parentElement
      // Before layout settles the map's canvas reports a zero box; keeping the starting box is
      // better than hiding the surface, and the next resize aligns it properly.
      if (!host) return
      const hostRect = host.getBoundingClientRect()
      const mapRect = mapCanvas.getBoundingClientRect()
      if (mapRect.width <= 0 || mapRect.height <= 0) return
      setStyleLength(canvas, 'left', mapRect.left - hostRect.left)
      setStyleLength(canvas, 'top', mapRect.top - hostRect.top)
      setStyleLength(canvas, 'width', mapRect.width)
      setStyleLength(canvas, 'height', mapRect.height)
    },
    dispose() {
      if (disposed) return
      disposed = true
      // dispose() frees Three's own resources but leaves the GL context alive; forceContextLoss
      // returns it to the browser, which caps how many can exist at once.
      renderer.forceContextLoss()
      renderer.dispose()
      canvas.remove()
    },
  }
}
