import { beforeEach, describe, expect, it, vi } from 'vitest'
import { rendererState, RendererState, type DrawingRequirement } from './rendererState'

function makeFakeRenderer() {
  return { shadowMap: { enabled: false } } as unknown as import('three').WebGLRenderer
}

describe('rendererState (FR-016, FR-017, research D3)', () => {
  let renderer: ReturnType<typeof makeFakeRenderer>
  let onConflict: ReturnType<typeof vi.fn>

  beforeEach(() => {
    renderer = makeFakeRenderer()
    onConflict = vi.fn()
    rendererState.bind(renderer, onConflict as (requirement: DrawingRequirement, requestedBy: string[]) => void)
  })

  it('applies a declared requirement onto the renderer', () => {
    rendererState.declareRequirement('ext-a', 'shadows')
    expect(renderer.shadowMap.enabled).toBe(true)
  })

  it('a requirement withdrawn by the only declarer stops being applied', () => {
    rendererState.declareRequirement('ext-a', 'shadows')
    rendererState.withdrawRequirements('ext-a')
    expect(renderer.shadowMap.enabled).toBe(false)
  })

  it('a requirement stays applied while at least one declarer remains', () => {
    rendererState.declareRequirement('ext-a', 'shadows')
    rendererState.declareRequirement('ext-b', 'shadows')
    rendererState.withdrawRequirements('ext-a')

    expect(renderer.shadowMap.enabled).toBe(true)
  })

  it('withdrawing a requirement one extension never declared has no effect', () => {
    rendererState.declareRequirement('ext-a', 'shadows')
    rendererState.withdrawRequirements('ext-b')
    expect(renderer.shadowMap.enabled).toBe(true)
  })

  it('reports a conflict via the bound callback rather than silently picking a side', () => {
    rendererState.reportConflict('shadows', ['ext-a', 'ext-b'])
    expect(onConflict).toHaveBeenCalledWith('shadows', ['ext-a', 'ext-b'])
  })

  // FOUND LIVE (2026-09-13): an extension can declare a requirement before the map bridge's
  // async `onContextRestored` ever calls `bind()` — a fresh instance (not the shared singleton
  // the `beforeEach` above always binds first) is needed to reproduce that ordering.
  it('applies a requirement declared before the renderer was ever bound', () => {
    const late = new RendererState()
    const lateRenderer = makeFakeRenderer()
    late.declareRequirement('ext-early', 'shadows')
    expect(lateRenderer.shadowMap.enabled).toBe(false) // nothing to apply onto yet

    late.bind(lateRenderer, vi.fn())
    expect(lateRenderer.shadowMap.enabled).toBe(true)
  })

  it('a requirement withdrawn before the renderer was ever bound never gets applied on bind', () => {
    const late = new RendererState()
    const lateRenderer = makeFakeRenderer()
    late.declareRequirement('ext-early', 'shadows')
    late.withdrawRequirements('ext-early')

    late.bind(lateRenderer, vi.fn())
    expect(lateRenderer.shadowMap.enabled).toBe(false)
  })
})
