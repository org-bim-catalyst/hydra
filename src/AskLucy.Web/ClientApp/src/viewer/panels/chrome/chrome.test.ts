import { describe, expect, it } from 'vitest'
import { MIN_PANEL_HEIGHT, MIN_PANEL_WIDTH } from '../types/panel'
import { DEFAULT_CONTENT_CHROME, resolveChrome } from './chrome'

describe('resolveChrome (data-model.md "Panel Chrome", research D7)', () => {
  it('falls back to the default content chrome when no override is given', () => {
    expect(resolveChrome(null)).toEqual(DEFAULT_CONTENT_CHROME)
    expect(resolveChrome(undefined)).toEqual(DEFAULT_CONTENT_CHROME)
  })

  it('applies a partial override on top of the base chrome', () => {
    const resolved = resolveChrome({ titleBar: false })
    expect(resolved.titleBar).toBe(false)
    expect(resolved.resizable).toBe(DEFAULT_CONTENT_CHROME.resizable)
  })

  it('uses a live panel kind\'s registered chrome as the base when given one', () => {
    const liveBase = { titleBar: true, resizable: false, defaultSize: { width: 320, height: 240 } }
    expect(resolveChrome(null, liveBase)).toEqual(liveBase)
    expect(resolveChrome({ resizable: true }, liveBase)).toEqual({ ...liveBase, resizable: true })
  })

  it('clamps the resolved default size to the minimum usable floor (spec Edge Cases)', () => {
    const resolved = resolveChrome({ defaultSize: { width: 10, height: 10 } })
    expect(resolved.defaultSize.width).toBe(MIN_PANEL_WIDTH)
    expect(resolved.defaultSize.height).toBe(MIN_PANEL_HEIGHT)
  })

  it('leaves a default size above the floor untouched', () => {
    const resolved = resolveChrome({ defaultSize: { width: 500, height: 400 } })
    expect(resolved.defaultSize).toEqual({ width: 500, height: 400 })
  })
})
