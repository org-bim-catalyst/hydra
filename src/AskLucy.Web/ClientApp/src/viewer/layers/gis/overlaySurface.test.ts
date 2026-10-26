import * as THREE from 'three'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createOverlaySurface } from './overlaySurface'

/** jsdom has no WebGL context, so every test injects this stand-in for the real renderer. */
function fakeRenderer() {
  const renderer = {
    setPixelRatio: vi.fn(),
    setClearColor: vi.fn(),
    setSize: vi.fn(),
    forceContextLoss: vi.fn(),
    dispose: vi.fn(),
    outputColorSpace: '',
    toneMapping: THREE.NoToneMapping,
    autoClear: true,
  }
  return { renderer, asThree: renderer as unknown as THREE.WebGLRenderer }
}

function container(): HTMLElement {
  const element = document.createElement('div')
  document.body.appendChild(element)
  return element
}

afterEach(() => {
  document.body.innerHTML = ''
})

describe('createOverlaySurface (specs/087 FR-004, FR-006)', () => {
  it('appends a transparent, non-interactive canvas that fills its container', () => {
    const host = container()
    const { asThree } = fakeRenderer()

    const surface = createOverlaySurface(host, () => asThree)

    expect(host.children).toContain(surface.canvas)
    expect(surface.canvas.style.position).toBe('absolute')
    // jsdom serialises a zero offset as either `0` or `0px`, depending on the shorthand used.
    for (const side of ['top', 'right', 'bottom', 'left'] as const) {
      expect(['0', '0px']).toContain(surface.canvas.style[side])
    }
    expect(surface.canvas.style.pointerEvents).toBe('none')
  })

  it('configures the renderer once: pixel ratio 1, transparent clear, sRGB + ACES', () => {
    const { renderer, asThree } = fakeRenderer()

    createOverlaySurface(container(), () => asThree)

    expect(renderer.setPixelRatio).toHaveBeenCalledWith(1)
    // The bridge renders two passes with its own depth clear between them.
    expect(renderer.autoClear).toBe(false)
    expect(renderer.setClearColor).toHaveBeenCalledWith(0x000000, 0)
    expect(renderer.outputColorSpace).toBe(THREE.SRGBColorSpace)
    expect(renderer.toneMapping).toBe(THREE.ACESFilmicToneMapping)
  })
})

describe('resize (specs/087 FR-004)', () => {
  it('sets the drawing buffer without touching the CSS size, and no-ops when unchanged', () => {
    const { renderer, asThree } = fakeRenderer()
    const surface = createOverlaySurface(container(), () => asThree)

    surface.resize(800, 600)
    surface.resize(800, 600)

    expect(renderer.setSize).toHaveBeenCalledTimes(1)
    expect(renderer.setSize).toHaveBeenCalledWith(800, 600, false)

    surface.resize(1024, 600)
    expect(renderer.setSize).toHaveBeenCalledTimes(2)
  })
})

describe('alignTo (specs/087 D4, corrected live 2026-10-26)', () => {
  function rect(left: number, top: number, width: number, height: number): DOMRect {
    return {
      left,
      top,
      width,
      height,
      right: left + width,
      bottom: top + height,
      x: left,
      y: top,
      toJSON: () => ({}),
    } as DOMRect
  }

  it('takes the canvas box from the map canvas, not from the container', () => {
    const host = container()
    host.getBoundingClientRect = () => rect(100, 50, 1600, 872)
    const mapCanvas = document.createElement('canvas')
    // The measured real-world case: the map's canvas is narrower and shorter than its container.
    mapCanvas.getBoundingClientRect = () => rect(100, 50, 1592, 868)
    const { asThree } = fakeRenderer()
    const surface = createOverlaySurface(host, () => asThree)

    surface.alignTo(mapCanvas)

    expect(surface.canvas.style.left).toBe('0px')
    expect(surface.canvas.style.top).toBe('0px')
    expect(surface.canvas.style.width).toBe('1592px')
    expect(surface.canvas.style.height).toBe('868px')
  })

  it('keeps the map canvas offset relative to the container', () => {
    const host = container()
    host.getBoundingClientRect = () => rect(100, 50, 1600, 872)
    const mapCanvas = document.createElement('canvas')
    mapCanvas.getBoundingClientRect = () => rect(104, 52, 1592, 868)
    const { asThree } = fakeRenderer()
    const surface = createOverlaySurface(host, () => asThree)

    surface.alignTo(mapCanvas)

    expect(surface.canvas.style.left).toBe('4px')
    expect(surface.canvas.style.top).toBe('2px')
  })

  it('leaves the starting box alone until the map canvas has been laid out', () => {
    const host = container()
    const mapCanvas = document.createElement('canvas')
    // jsdom reports a zero box for everything; a real browser does the same before layout.
    const { asThree } = fakeRenderer()
    const surface = createOverlaySurface(host, () => asThree)

    surface.alignTo(mapCanvas)

    expect(surface.canvas.style.width).toBe('')
    expect(surface.canvas.style.height).toBe('')
  })
})

describe('dispose (specs/087 FR-009, SC-005)', () => {
  it('releases the context, disposes the renderer and removes the canvas once', () => {
    const host = container()
    const { renderer, asThree } = fakeRenderer()
    const surface = createOverlaySurface(host, () => asThree)

    surface.dispose()
    surface.dispose()

    expect(renderer.forceContextLoss).toHaveBeenCalledTimes(1)
    expect(renderer.dispose).toHaveBeenCalledTimes(1)
    expect(host.children).not.toContain(surface.canvas)
  })

  it('resize after dispose does nothing', () => {
    const { renderer, asThree } = fakeRenderer()
    const surface = createOverlaySurface(container(), () => asThree)

    surface.dispose()
    surface.resize(10, 10)

    expect(renderer.setSize).not.toHaveBeenCalled()
  })

  it('a renderer that cannot be created leaves no canvas behind and propagates', () => {
    const host = container()

    expect(() =>
      createOverlaySurface(host, () => {
        throw new Error('no WebGL context')
      }),
    ).toThrow('no WebGL context')

    expect(host.children).toHaveLength(0)
  })
})
