import { describe, expect, it } from 'vitest'
import { z } from 'zod'
import { panelTypeRegistry } from './registry'
import type { PanelTypeDefinition } from './types/panel'

function makeDefinition(typeKey: string): PanelTypeDefinition<{ label: string }> {
  return {
    typeKey,
    renderer: () => null,
    schema: z.object({ label: z.string() }),
    defaultSize: { width: 320, height: 240 },
    resizable: true,
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
})
