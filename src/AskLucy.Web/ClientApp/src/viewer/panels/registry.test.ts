import { describe, expect, it } from 'vitest'
import { z } from 'zod'
import { panelTypeRegistry } from './registry'
import type { PanelTypeDefinition } from './types/panel'

function makeDefinition(typeKey: string): PanelTypeDefinition<{ label: string }> {
  return {
    typeKey,
    renderer: () => null,
    schema: z.object({ label: z.string() }),
    chrome: { titleBar: true, resizable: true, defaultSize: { width: 320, height: 240 } },
  }
}

describe('panelTypeRegistry', () => {
  it('resolves an unregistered type key to undefined', () => {
    expect(panelTypeRegistry.resolve('does-not-exist-' + Math.random())).toBeUndefined()
  })

  it('registers a type and resolves it back by key', () => {
    const key = `test-type-${Math.random()}`
    const definition = makeDefinition(key)
    panelTypeRegistry.register(definition)
    expect(panelTypeRegistry.resolve(key)).toBe(definition)
  })

  it('throws in dev mode when the same typeKey is registered twice', () => {
    const key = `duplicate-type-${Math.random()}`
    panelTypeRegistry.register(makeDefinition(key))
    expect(() => panelTypeRegistry.register(makeDefinition(key))).toThrow(/already registered/)
  })

  it('withdraws a registered kind, so it no longer resolves (specs/049 T061, preparing for specs/050 extension stop)', () => {
    const key = `withdrawable-type-${Math.random()}`
    panelTypeRegistry.register(makeDefinition(key))
    expect(panelTypeRegistry.resolve(key)).toBeDefined()

    panelTypeRegistry.unregister(key)

    expect(panelTypeRegistry.resolve(key)).toBeUndefined()
  })

  it('treats unregistering a key that was never registered as a no-op', () => {
    expect(() => panelTypeRegistry.unregister('never-registered-' + Math.random())).not.toThrow()
  })

  it('allows re-registering a key after it has been withdrawn', () => {
    const key = `re-register-type-${Math.random()}`
    panelTypeRegistry.register(makeDefinition(key))
    panelTypeRegistry.unregister(key)

    expect(() => panelTypeRegistry.register(makeDefinition(key))).not.toThrow()
    expect(panelTypeRegistry.resolve(key)).toBeDefined()
  })
})
