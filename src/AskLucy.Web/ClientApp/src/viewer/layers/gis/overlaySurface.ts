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
  /** Releases the context, every GPU resource and the canvas. Safe to call twice. */
  dispose(): void
}

/** `pointer-events: none` keeps every map gesture with the map; the four offsets match the map's
 * own full-size box (the container Maps is constructed in), so container resizes need no
 * listener. The z-index puts the canvas above Maps' own canvas, which it renders at z-index 0. */
const CANVAS_STYLE = 'position:absolute;top:0;right:0;bottom:0;left:0;pointer-events:none;z-index:1;'

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
