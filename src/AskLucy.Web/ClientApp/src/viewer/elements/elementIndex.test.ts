import { describe, expect, it } from 'vitest'
import { buildElementIndex, getElementProperties } from './elementIndex'

describe('elementIndex (FR-027, FR-028, FR-031)', () => {
  it('a node with properties becomes readable', () => {
    const index = buildElementIndex([{ elementId: 'wall-1', properties: { material: 'concrete' } }])
    expect(getElementProperties(index, 'wall-1')).toEqual({
      hasProperties: true,
      properties: { material: 'concrete' },
    })
  })

  it('a node with no entry reports hasProperties: false, never an empty object', () => {
    const index = buildElementIndex([])
    const result = getElementProperties(index, 'unknown-element')
    expect(result).toEqual({ hasProperties: false, properties: null })
    expect(result.properties).not.toEqual({})
  })

  it('an undefined index (content with no element information at all) reports hasProperties: false', () => {
    expect(getElementProperties(undefined, 'any-id')).toEqual({ hasProperties: false, properties: null })
  })
})
