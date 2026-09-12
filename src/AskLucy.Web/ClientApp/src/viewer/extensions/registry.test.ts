import { describe, expect, it } from 'vitest'
import { viewerExtensionRegistry } from './registry'
import type { ViewerExtension } from './ViewerExtension'

function makeExtension(id: string): ViewerExtension {
  return {
    id,
    manifest: { displayName: id, description: 'test' },
    start: () => {},
    stop: () => {},
  }
}

describe('viewerExtensionRegistry', () => {
  it('resolves an unregistered id to undefined rather than throwing', () => {
    expect(viewerExtensionRegistry.resolve('does-not-exist-' + Math.random())).toBeUndefined()
  })

  it('registers an extension and resolves it back by id', () => {
    const id = `test-ext-${Math.random()}`
    const extension = makeExtension(id)
    viewerExtensionRegistry.register(extension)
    expect(viewerExtensionRegistry.resolve(id)).toBe(extension)
  })

  it('throws in dev mode when the same id is registered twice', () => {
    const id = `duplicate-ext-${Math.random()}`
    viewerExtensionRegistry.register(makeExtension(id))
    expect(() => viewerExtensionRegistry.register(makeExtension(id))).toThrow(/already registered/)
  })
})
