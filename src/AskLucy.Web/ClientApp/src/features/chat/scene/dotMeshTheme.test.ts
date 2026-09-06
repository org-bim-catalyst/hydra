import { describe, expect, it } from 'vitest'
import { getDotMeshColors } from './dotMeshTheme'

describe('getDotMeshColors', () => {
  it('returns the light-mode gold palette for legibility against a light background', () => {
    expect(getDotMeshColors('light')).toEqual({ idle: '#8A6B12', reactive: '#B8860B' })
  })

  it('returns the dark-mode gold palette for legibility against a dark background', () => {
    expect(getDotMeshColors('dark')).toEqual({ idle: '#C9A227', reactive: '#FFC94A' })
  })

  it('returns distinct colors per mode (FR-008 — colors must actually change with the theme)', () => {
    const light = getDotMeshColors('light')
    const dark = getDotMeshColors('dark')

    expect(light.idle).not.toBe(dark.idle)
    expect(light.reactive).not.toBe(dark.reactive)
  })
})
