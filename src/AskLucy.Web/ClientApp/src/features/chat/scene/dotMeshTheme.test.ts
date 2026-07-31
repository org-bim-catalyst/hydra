import { describe, expect, it } from 'vitest'
import { getDotMeshColors } from './dotMeshTheme'

describe('getDotMeshColors', () => {
  it('returns the light-mode palette (primary.dark / secondary.dark) for legibility against a light background', () => {
    expect(getDotMeshColors('light')).toEqual({ idle: '#123340', reactive: '#7E2E12' })
  })

  it('returns the dark-mode palette (primary.light / secondary.light) for legibility against a dark background', () => {
    expect(getDotMeshColors('dark')).toEqual({ idle: '#4C7B8B', reactive: '#D97650' })
  })

  it('returns distinct colors per mode (FR-008 — colors must actually change with the theme)', () => {
    const light = getDotMeshColors('light')
    const dark = getDotMeshColors('dark')

    expect(light.idle).not.toBe(dark.idle)
    expect(light.reactive).not.toBe(dark.reactive)
  })
})
