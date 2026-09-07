import { describe, expect, it } from 'vitest'
import { getDotMeshColors } from './dotMeshTheme'

describe('getDotMeshColors', () => {
  it('returns the light-mode cyan palette for legibility against a light background', () => {
    expect(getDotMeshColors('light')).toEqual({ idle: '#0E7490', reactive: '#0891B2' })
  })

  it('returns the dark-mode cyan palette for legibility against a dark background', () => {
    expect(getDotMeshColors('dark')).toEqual({ idle: '#22D3EE', reactive: '#A5FFFB' })
  })

  it('returns distinct colors per mode (FR-008 — colors must actually change with the theme)', () => {
    const light = getDotMeshColors('light')
    const dark = getDotMeshColors('dark')

    expect(light.idle).not.toBe(dark.idle)
    expect(light.reactive).not.toBe(dark.reactive)
  })
})
