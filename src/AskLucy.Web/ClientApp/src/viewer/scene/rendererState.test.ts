import { beforeEach, describe, expect, it, vi } from 'vitest'
import { rendererState, type DrawingRequirement } from './rendererState'

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
})
